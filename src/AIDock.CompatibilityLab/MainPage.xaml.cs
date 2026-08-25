using System.ComponentModel;
using System.Diagnostics;
using AIDrawer.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.System;

namespace AIDock.CompatibilityLab;

public sealed partial class MainPage : Page
{
    private readonly DispatcherTimer _metricsTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly SanitizedDiagnosticLog _diagnosticLog = new();
    private WebView2? _webView;
    private CoreWebView2Environment? _environment;
    private ProviderDefinition? _currentProvider;
    private string? _profilePath;
    private string? _freshProfilePath;
    private string? _freshProfileRoot;
    private bool _observationMode;
    private DateTimeOffset _previousSampleTime;
    private TimeSpan _previousProcessorTime;

    public MainPage()
    {
        InitializeComponent();

        ProviderBox.ItemsSource = ProviderCatalog.InitialCandidates;
        ProviderBox.SelectedItem = ProviderCatalog.InitialCandidates.Single(provider => provider.Id == "gemini");

        ProfileRootBox.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AI Drawer",
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
            StartButton.IsEnabled = false;
            await EndTestAsync();
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
            _observationMode = ObservationModeBox.IsChecked == true;
            if (_observationMode && !string.Equals(mode, "fresh", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Provider observation mode requires a fresh disposable profile.");
            }

            _currentProvider = provider;
            _profilePath = BuildProfilePath(root, provider, mode);
            _freshProfilePath = mode == "fresh" ? _profilePath : null;
            _freshProfileRoot = mode == "fresh" ? root : null;

            Directory.CreateDirectory(_profilePath);
            ProfilePathText.Text = $"Profile: {_profilePath}";
            SetConfigurationEnabled(false);

            await InitializeWebViewAsync(provider, _profilePath);
            StartButton.Content = "End test";
            StartButton.IsEnabled = true;
        }
        catch (Exception exception)
        {
            var failedFreshProfilePath = _freshProfilePath;
            var failedFreshProfileRoot = _freshProfileRoot;
            CloseWebView();
            _environment = null;
            _currentProvider = null;
            _profilePath = null;
            _freshProfilePath = null;
            _freshProfileRoot = null;
            _observationMode = false;
            ObservationModeBox.IsChecked = false;
            ProfilePathText.Text = "Profile: not started";
            CurrentOriginText.Text = "Current origin: not started";
            SetConfigurationEnabled(true);
            UpdateSelectedProviderUi();
            Log($"start-failed {exception.GetType().Name}");
            await DeleteFreshProfileAfterReleaseAsync(failedFreshProfileRoot, failedFreshProfilePath);
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

        Log($"webview-ready provider={provider.Id}");
        _webView.CoreWebView2.Navigate(provider.HomeUri.AbsoluteUri);
    }

    private void ConfigureWebView(CoreWebView2 core, ProviderDefinition provider)
    {
        var settings = WebViewSecurityPolicy.EmbeddedProviderDefaults;
        core.Settings.AreDevToolsEnabled = settings.AreDevToolsEnabled;
        core.Settings.AreHostObjectsAllowed = settings.AreHostObjectsAllowed;
        core.Settings.IsWebMessageEnabled = settings.IsWebMessageEnabled;
        core.Settings.IsPasswordAutosaveEnabled = settings.IsPasswordAutosaveEnabled;
        core.Settings.IsGeneralAutofillEnabled = settings.IsGeneralAutofillEnabled;

        core.NavigationStarting += (_, args) =>
        {
            CurrentOriginText.Text = $"Current origin: {ProviderDefinition.CreateOriginLabel(args.Uri)}";
            if (provider.IsKnownPurchaseUri(args.Uri))
            {
                args.Cancel = true;
                Log("purchase-navigation-blocked");
                return;
            }

            if (!provider.IsProviderAppUri(args.Uri)
                && !(_observationMode && provider.IsSafeObservationUri(args.Uri)))
            {
                args.Cancel = true;
                Log($"navigation-blocked scheme={SafeEventText.SchemeCategory(args.Uri)}");
                return;
            }

            Log(_observationMode
                ? $"navigation-observation scheme={SafeEventText.SchemeCategory(args.Uri)}"
                : $"navigation-start scheme={SafeEventText.SchemeCategory(args.Uri)}");
        };

        core.SourceChanged += (_, _) =>
            CurrentOriginText.Text = $"Current origin: {ProviderDefinition.CreateOriginLabel(core.Source)}";

        core.NavigationCompleted += (_, args) =>
            Log(args.IsSuccess
                ? "navigation-complete success"
                : $"navigation-complete failure={args.WebErrorStatus}");

        core.NewWindowRequested += (_, args) =>
        {
            if (provider.IsKnownPurchaseUri(args.Uri))
            {
                args.Handled = true;
                Log("purchase-popup-blocked");
                return;
            }

            if (_observationMode && provider.IsSafeObservationUri(args.Uri))
            {
                Log($"popup-observation scheme={SafeEventText.SchemeCategory(args.Uri)}");
                return;
            }

            args.Handled = true;
            Log($"popup-blocked scheme={SafeEventText.SchemeCategory(args.Uri)}");
        };

        core.PermissionRequested += (_, args) =>
        {
            args.SavesInProfile = false;
            if (!_observationMode)
            {
                args.State = CoreWebView2PermissionState.Deny;
            }

            Log($"permission-{(_observationMode ? "observation" : "blocked")} kind={args.PermissionKind}");
        };

        core.DownloadStarting += (_, args) =>
        {
            if (!_observationMode)
            {
                args.Cancel = true;
                args.Handled = true;
            }

            Log(_observationMode ? "download-observation" : "download-blocked");
        };

        core.ServerCertificateErrorDetected += (_, args) =>
        {
            args.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
            Log("certificate-error-blocked");
        };

        core.FrameCreated += (_, args) =>
        {
            args.Frame.NavigationStarting += (_, frameArgs) =>
            {
                if (provider.IsKnownPurchaseUri(frameArgs.Uri)
                    || (!provider.IsProviderAppUri(frameArgs.Uri)
                        && !(_observationMode && provider.IsSafeObservationUri(frameArgs.Uri))))
                {
                    frameArgs.Cancel = true;
                    Log("frame-navigation-blocked");
                }
            };
        };

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
        ObservationModeBox.IsEnabled = isEnabled;
    }

    private async Task EndTestAsync()
    {
        var freshProfilePath = _freshProfilePath;
        var freshProfileRoot = _freshProfileRoot;
        CloseWebView();
        _environment = null;
        _currentProvider = null;
        _profilePath = null;
        _freshProfilePath = null;
        _freshProfileRoot = null;
        _observationMode = false;
        ObservationModeBox.IsChecked = false;
        ProfilePathText.Text = "Profile: not started";
        CurrentOriginText.Text = "Current origin: not started";
        SetConfigurationEnabled(true);
        UpdateSelectedProviderUi();
        Log("webview-closed");
        await DeleteFreshProfileAfterReleaseAsync(freshProfileRoot, freshProfilePath);
        StartButton.IsEnabled = true;
    }

    private async Task DeleteFreshProfileAfterReleaseAsync(string? root, string? profilePath)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(profilePath))
        {
            return;
        }

        if (!IsSafeFreshProfilePath(root, profilePath))
        {
            Log("fresh-profile-cleanup-skipped unsafe-path");
            return;
        }

        for (var attempt = 1; attempt <= 12; attempt++)
        {
            try
            {
                if (Directory.Exists(profilePath))
                {
                    Directory.Delete(profilePath, recursive: true);
                }

                Log("fresh-profile-cleanup-complete");
                return;
            }
            catch (IOException) when (attempt < 12)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }
            catch (UnauthorizedAccessException)
            {
                Log("fresh-profile-cleanup-deferred access-denied");
                return;
            }
            catch (IOException)
            {
                Log("fresh-profile-cleanup-deferred process-still-releasing");
            }
        }
    }

    private static bool IsSafeFreshProfilePath(string root, string profilePath)
    {
        try
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullProfile = Path.GetFullPath(profilePath);
            var relative = Path.GetRelativePath(fullRoot, fullProfile);
            if (relative.StartsWith("..", StringComparison.Ordinal)
                || Path.IsPathRooted(relative)
                || !Path.GetFileName(fullProfile).StartsWith("fresh-", StringComparison.Ordinal))
            {
                return false;
            }

            var current = fullRoot;
            if (IsReparsePoint(current))
            {
                return false;
            }

            foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                current = Path.Combine(current, segment);
                if (IsReparsePoint(current))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsReparsePoint(string path) =>
        Directory.Exists(path)
        && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        _diagnosticLog.Clear();
        EventLogBox.Text = string.Empty;
    }

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

    private async void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        var freshProfilePath = _freshProfilePath;
        var freshProfileRoot = _freshProfileRoot;
        _metricsTimer.Stop();
        CloseWebView();
        _environment = null;
        _freshProfilePath = null;
        _freshProfileRoot = null;
        _observationMode = false;
        await DeleteFreshProfileAfterReleaseAsync(freshProfileRoot, freshProfilePath);
    }

    private void CloseWebView()
    {
        _metricsTimer.Stop();
        _webView?.Close();
        _webView = null;
        CurrentOriginText.Text = "Current origin: not started";
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
        _diagnosticLog.Record(message);
        EventLogBox.Text = _diagnosticLog.Render();
        EventLogBox.Select(EventLogBox.Text.Length, 0);
    }
}
