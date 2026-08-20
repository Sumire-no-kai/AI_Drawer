using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AIDrawer;

public sealed partial class MainPage : Page
{
    private GeminiWorkspace? _workspace;
    private bool _hasLoaded;

    public MainPage()
    {
        InitializeComponent();
    }

    internal void SetShortcutState(bool registered, int errorCode)
    {
        if (registered)
        {
            return;
        }

        StatusBar.Title = "Global shortcut unavailable";
        StatusBar.Message = $"Win + Shift + A could not be registered (Windows error {errorCode}).";
        StatusBar.Severity = InfoBarSeverity.Warning;
    }

    internal void DisposeWorkspace()
    {
        _workspace?.Dispose();
        _workspace = null;
    }

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_hasLoaded)
        {
            return;
        }

        _hasLoaded = true;
        _workspace = new GeminiWorkspace(WebViewHost);
        _workspace.StateChanged += Workspace_StateChanged;
        await _workspace.StartAsync();
    }

    private void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_hasLoaded)
        {
            DisposeWorkspace();
            _hasLoaded = false;
        }
    }

    private void Workspace_StateChanged(object? sender, WorkspaceStateChangedEventArgs args)
    {
        if (args.Severity == InfoBarSeverity.Success)
        {
            StatusBar.IsOpen = false;
            RecoveryPanel.Visibility = Visibility.Collapsed;
            return;
        }

        StatusBar.Title = args.Title;
        StatusBar.Message = args.Message;
        StatusBar.Severity = args.Severity;
        StatusBar.IsOpen = true;

        RecoveryMessage.Text = args.Message;
        RecoveryPanel.Visibility = args.RequiresRecovery ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        RecoveryPanel.Visibility = Visibility.Collapsed;
        _workspace?.Reload();
    }

    private async void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        RecoveryPanel.Visibility = Visibility.Collapsed;
        if (_workspace is not null)
        {
            await _workspace.RestartAsync();
        }
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e) => App.ExitApplication();
}
