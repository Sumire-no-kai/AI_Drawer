using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace AIDrawer;

public sealed partial class MainPage : Page
{
    private WorkspaceCoordinator? _workspaceCoordinator;
    private bool _hasLoaded;
    private bool _isUpdatingSelection;

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
        _workspaceCoordinator?.Dispose();
        _workspaceCoordinator = null;
    }

    internal void SetWindowVisibility(bool isVisible)
    {
        _workspaceCoordinator?.SetWindowVisibility(isVisible);
    }

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_hasLoaded)
        {
            return;
        }

        _hasLoaded = true;
        _workspaceCoordinator = new WorkspaceCoordinator(WebViewHost, RequestPermissionAsync);
        _workspaceCoordinator.StateChanged += Workspace_StateChanged;
        ProviderSelector.ItemsSource = _workspaceCoordinator.Providers;
        await SelectProviderAsync("gemini");
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
        if (!string.Equals(_workspaceCoordinator?.ActiveWorkspace?.Provider.Id, args.ProviderId, StringComparison.Ordinal))
        {
            return;
        }

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

        RecoveryTitle.Text = args.Title;
        RecoveryMessage.Text = args.Message;
        RecoveryPanel.Visibility = args.RequiresRecovery ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        RecoveryPanel.Visibility = Visibility.Collapsed;
        _workspaceCoordinator?.ReloadActiveWorkspace();
    }

    private async void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        RecoveryPanel.Visibility = Visibility.Collapsed;
        if (_workspaceCoordinator is not null)
        {
            await _workspaceCoordinator.RestartActiveWorkspaceAsync();
        }
    }

    private async void ProviderSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUpdatingSelection && ProviderSelector.SelectedItem is ProviderDefinition provider)
        {
            await SelectProviderAsync(provider.Id);
        }
    }

    private async void ProviderShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        var providerIndex = sender.Key switch
        {
            VirtualKey.Number1 => 0,
            VirtualKey.Number2 => 1,
            VirtualKey.Number3 => 2,
            VirtualKey.Number4 => 3,
            VirtualKey.Number5 => 4,
            VirtualKey.Number6 => 5,
            VirtualKey.Number7 => 6,
            VirtualKey.Number8 => 7,
            _ => -1
        };

        if (_workspaceCoordinator is not null && providerIndex >= 0 && providerIndex < _workspaceCoordinator.Providers.Count)
        {
            args.Handled = true;
            await SelectProviderAsync(_workspaceCoordinator.Providers[providerIndex].Id);
        }
    }

    private async void ResetWebsiteDataMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_workspaceCoordinator?.ActiveWorkspace is not { } workspace)
        {
            return;
        }

        var confirmation = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Reset {workspace.Provider.DisplayName} website data?",
            Content = "This signs you out of this local AI Drawer workspace and removes its local cookies, cache, site storage, and remembered permissions. Your provider account and provider-hosted conversations are not deleted.",
            PrimaryButtonText = "Reset website data",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await _workspaceCoordinator.ResetActiveWorkspaceAsync();
        await SelectProviderAsync(workspace.Provider.Id);
    }

    private async Task SelectProviderAsync(string providerId)
    {
        if (_workspaceCoordinator is null)
        {
            return;
        }

        var provider = _workspaceCoordinator.Providers.Single(candidate => candidate.Id == providerId);
        _isUpdatingSelection = true;
        ProviderSelector.SelectedItem = provider;
        _isUpdatingSelection = false;
        var providerIndex = _workspaceCoordinator.Providers.ToList().IndexOf(provider) + 1;
        CompatibilityStatusText.Text = $"{provider.CompatibilityStatus} · Ctrl + {providerIndex}";
        RecoveryPanel.Visibility = Visibility.Collapsed;
        await _workspaceCoordinator.SelectAsync(providerId);
    }

    private async Task<PermissionDecision> RequestPermissionAsync(PermissionRequest request)
    {
        var rememberDecision = new CheckBox { Content = "Remember this decision for this workspace" };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Allow {request.PermissionKind} for {request.ProviderName}?",
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = "The provider requested a privileged browser permission. AI Drawer will not grant it automatically.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    rememberDecision
                }
            },
            PrimaryButtonText = "Allow",
            CloseButtonText = "Deny",
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        return new PermissionDecision(result == ContentDialogResult.Primary, rememberDecision.IsChecked == true);
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e) => App.ExitApplication();
}
