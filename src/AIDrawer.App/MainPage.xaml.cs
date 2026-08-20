using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.System;

namespace AIDrawer;

public sealed partial class MainPage : Page
{
    private readonly Dictionary<string, WorkspaceTabView> _workspaceTabViews = new(StringComparer.Ordinal);
    private readonly List<WorkspaceTab> _workspaces = [];
    private WorkspaceCoordinator? _workspaceCoordinator;
    private WorkspaceTab? _activeWorkspace;
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
        StatusBar.IsOpen = true;
    }

    internal void DisposeWorkspace()
    {
        _workspaceCoordinator?.Dispose();
        _workspaceCoordinator = null;
    }

    internal void SetWindowVisibility(bool isVisible) => _workspaceCoordinator?.SetWindowVisibility(isVisible);

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_hasLoaded)
        {
            return;
        }

        _hasLoaded = true;
        _workspaceCoordinator = new WorkspaceCoordinator(WebViewHost, RequestPermissionAsync);
        _workspaceCoordinator.StateChanged += Workspace_StateChanged;
        PopulateProviderChooser();
        await CreateWorkspaceAsync();
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
        if (!string.Equals(_activeWorkspace?.Id, args.WorkspaceId, StringComparison.Ordinal))
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

    private async void NewWorkspaceButton_Click(object sender, RoutedEventArgs e) => await CreateWorkspaceAsync();

    private async void NewWorkspaceShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await CreateWorkspaceAsync();
    }

    private async void WorkspaceTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string workspaceId })
        {
            await SelectWorkspaceAsync(workspaceId);
        }
    }

    private async void CloseWorkspaceTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string workspaceId }
            || _workspaces.Count == 1
            || _workspaces.FirstOrDefault(workspace => workspace.Id == workspaceId) is not { } workspace)
        {
            return;
        }

        var wasActive = ReferenceEquals(workspace, _activeWorkspace);
        if (_workspaceCoordinator is not null)
        {
            await _workspaceCoordinator.RemoveWorkspaceAsync(workspace.Id);
        }
        _workspaces.Remove(workspace);

        if (_workspaceTabViews.Remove(workspace.Id, out var tabView))
        {
            WorkspaceTabs.Children.Remove(tabView.Container);
        }

        UpdateCloseButtonVisibility();
        if (wasActive)
        {
            await SelectWorkspaceAsync(_workspaces.Last().Id);
        }
    }

    private async void ProviderChoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string providerId })
        {
            await OpenProviderInActiveWorkspaceAsync(providerId);
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

        if (_workspaceCoordinator is null || providerIndex < 0 || providerIndex >= _workspaceCoordinator.Providers.Count)
        {
            return;
        }

        args.Handled = true;
        var provider = _workspaceCoordinator.Providers[providerIndex];
        var existingWorkspace = _workspaces.LastOrDefault(workspace => workspace.Provider?.Id == provider.Id);
        if (existingWorkspace is not null)
        {
            await SelectWorkspaceAsync(existingWorkspace.Id);
            return;
        }

        if (_activeWorkspace?.IsHome == true)
        {
            await OpenProviderInActiveWorkspaceAsync(provider.Id);
            return;
        }

        await CreateWorkspaceAsync();
        await OpenProviderInActiveWorkspaceAsync(provider.Id);
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
            Content = "This clears the local cookies, cache, site storage, and remembered permissions shared by every AI Drawer workspace using this provider. Those workspaces may need to reload. Your provider account and provider-hosted conversations are not deleted.",
            PrimaryButtonText = "Reset website data",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await _workspaceCoordinator.ResetActiveWorkspaceAsync();
        if (_activeWorkspace is not null)
        {
            await SelectWorkspaceAsync(_activeWorkspace.Id);
        }
    }

    private async Task CreateWorkspaceAsync()
    {
        var workspace = new WorkspaceTab();
        _workspaces.Add(workspace);
        AddWorkspaceTab(workspace);
        UpdateCloseButtonVisibility();
        await SelectWorkspaceAsync(workspace.Id);
    }

    private async Task OpenProviderInActiveWorkspaceAsync(string providerId)
    {
        if (_workspaceCoordinator is null || _activeWorkspace is null || !_activeWorkspace.IsHome)
        {
            return;
        }

        var provider = _workspaceCoordinator.Providers.Single(candidate => candidate.Id == providerId);
        var workspaceNumber = GetNextWorkspaceNumber(provider);
        _activeWorkspace.SelectProvider(provider, workspaceNumber);
        UpdateWorkspaceTab(_activeWorkspace);
        await SelectWorkspaceAsync(_activeWorkspace.Id);
    }

    private async Task SelectWorkspaceAsync(string workspaceId)
    {
        if (_workspaceCoordinator is null || _workspaces.FirstOrDefault(workspace => workspace.Id == workspaceId) is not { } workspace)
        {
            return;
        }

        _activeWorkspace = workspace;
        UpdateWorkspaceTabSelection();
        RecoveryPanel.Visibility = Visibility.Collapsed;

        if (workspace.IsHome)
        {
            _workspaceCoordinator.DeactivateActiveWorkspace();
            HomePanel.Visibility = Visibility.Visible;
            WebViewHost.Visibility = Visibility.Collapsed;
            CompatibilityStatusText.Visibility = Visibility.Collapsed;
            WorkspaceActionsButton.IsEnabled = false;
            StatusBar.IsOpen = false;
            return;
        }

        HomePanel.Visibility = Visibility.Collapsed;
        WebViewHost.Visibility = Visibility.Visible;
        var provider = workspace.Provider ?? throw new InvalidOperationException("A non-home workspace must have a provider.");
        CompatibilityStatusText.Text = provider.CompatibilityStatus;
        CompatibilityStatusText.Visibility = Visibility.Visible;
        ReloadMenuItem.Text = $"Reload {provider.DisplayName}";
        RestartMenuItem.Text = $"Restart {provider.DisplayName} workspace";
        WorkspaceActionsButton.IsEnabled = true;
        await _workspaceCoordinator.ActivateAsync(workspace.Id, provider);
    }

    private void PopulateProviderChooser()
    {
        if (_workspaceCoordinator is null)
        {
            return;
        }

        ProviderChooser.Children.Clear();
        for (var providerIndex = 0; providerIndex < _workspaceCoordinator.Providers.Count; providerIndex++)
        {
            var provider = _workspaceCoordinator.Providers[providerIndex];
            var rowContent = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(42) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = new GridLength(96) },
                    new ColumnDefinition { Width = new GridLength(72) }
                }
            };

            rowContent.Children.Add(CreateProviderMark(provider));

            var name = new TextBlock
            {
                Text = provider.DisplayName,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(name, 1);
            rowContent.Children.Add(name);

            var status = new TextBlock
            {
                Text = provider.CompatibilityStatus,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(status, 2);
            rowContent.Children.Add(status);

            var shortcut = new TextBlock
            {
                Text = $"Ctrl+{providerIndex + 1}",
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12
            };
            Grid.SetColumn(shortcut, 3);
            rowContent.Children.Add(shortcut);

            var row = new Button
            {
                Tag = provider.Id,
                Style = (Style)Resources["ProviderChoiceButtonStyle"],
                Content = rowContent
            };
            ToolTipService.SetToolTip(row, $"Open {provider.DisplayName} in this workspace");
            AutomationProperties.SetName(row, $"Open {provider.DisplayName} workspace");
            row.Click += ProviderChoice_Click;

            ProviderChooser.Children.Add(new Border
            {
                BorderBrush = (Brush)Resources["ProviderTableBorderBrush"],
                BorderThickness = providerIndex == _workspaceCoordinator.Providers.Count - 1
                    ? new Thickness(0)
                    : new Thickness(0, 0, 0, 1),
                Child = row
            });
        }
    }

    private static FrameworkElement CreateProviderMark(ProviderDefinition provider)
    {
        if (provider.IconAssetUri is not null)
        {
            var iconUri = new Uri(provider.IconAssetUri);
            ImageSource iconSource = provider.IconAssetUri.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                ? new SvgImageSource(iconUri)
                : new BitmapImage(iconUri);

            if (provider.UsesMonochromeMark)
            {
                return new ImageIcon
                {
                    Width = 26,
                    Height = 26,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
                    Source = iconSource
                };
            }

            return new Image
            {
                Width = 26,
                Height = 26,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Source = iconSource
            };
        }

        return new Border
        {
            Width = 28,
            Height = 28,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"],
            CornerRadius = new CornerRadius(6),
            Child = new TextBlock
            {
                Text = provider.IconFallback,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = provider.IconFallback.Length > 2 ? 9 : 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            }
        };
    }

    private int GetNextWorkspaceNumber(ProviderDefinition provider)
    {
        var workspaceNumber = 1;
        while (_workspaces.Any(workspace =>
                   workspace.Provider?.Id == provider.Id
                   && string.Equals(
                       workspace.DisplayName,
                       workspaceNumber == 1
                           ? provider.WorkspaceLabel
                           : $"{provider.WorkspaceLabel} {workspaceNumber}",
                       StringComparison.Ordinal)))
        {
            workspaceNumber++;
        }

        return workspaceNumber;
    }

    private void AddWorkspaceTab(WorkspaceTab workspace)
    {
        var container = new Grid();
        var tab = new Button
        {
            Tag = workspace.Id,
            Content = workspace.DisplayName,
            Style = (Style)Resources["WorkspaceTabButtonStyle"]
        };
        tab.Click += WorkspaceTab_Click;

        var close = new Button
        {
            Tag = workspace.Id,
            Style = (Style)Resources["TabCloseButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Content = new FontIcon { Glyph = "\uE711", FontSize = 10 }
        };
        ToolTipService.SetToolTip(close, "Close workspace");
        AutomationProperties.SetName(close, $"Close {workspace.DisplayName}");
        close.Click += CloseWorkspaceTab_Click;

        container.Children.Add(tab);
        container.Children.Add(close);
        WorkspaceTabs.Children.Add(container);
        _workspaceTabViews.Add(workspace.Id, new WorkspaceTabView(container, tab, close));
    }

    private void UpdateWorkspaceTab(WorkspaceTab workspace)
    {
        if (_workspaceTabViews.TryGetValue(workspace.Id, out var tabView))
        {
            tabView.Tab.Content = workspace.DisplayName;
            AutomationProperties.SetName(tabView.Close, $"Close {workspace.DisplayName}");
        }
    }

    private void UpdateWorkspaceTabSelection()
    {
        var activeBrush = (Brush)Resources["WorkspaceTabActiveBrush"];
        foreach (var workspace in _workspaces)
        {
            if (!_workspaceTabViews.TryGetValue(workspace.Id, out var tabView))
            {
                continue;
            }

            var isActive = ReferenceEquals(workspace, _activeWorkspace);
            tabView.Tab.Background = isActive ? activeBrush : null;
            tabView.Tab.FontWeight = isActive ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        }
    }

    private void UpdateCloseButtonVisibility()
    {
        var visibility = _workspaces.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var tabView in _workspaceTabViews.Values)
        {
            tabView.Close.Visibility = visibility;
        }
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

    private sealed record WorkspaceTabView(Grid Container, Button Tab, Button Close);
}
