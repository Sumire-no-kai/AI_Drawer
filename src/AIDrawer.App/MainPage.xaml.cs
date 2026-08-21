using AIDrawer.Core;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Numerics;
using Windows.System;
using Windows.UI.ViewManagement;

namespace AIDrawer;

public sealed partial class MainPage : Page
{
    private readonly Dictionary<string, WorkspaceTabView> _workspaceTabViews = new(StringComparer.Ordinal);
    private readonly List<WorkspaceTab> _workspaces = [];
    private readonly UISettings _uiSettings = new();
    private readonly WorkspaceSessionStore _sessionStore = new();
    private WorkspaceCoordinator? _workspaceCoordinator;
    private WorkspaceTab? _activeWorkspace;
    private AppSettings _settings = new();
    private TaskCompletionSource<PromptDecision>? _promptCompletion;
    private bool _hasLoaded;
    private bool _updatingSettingsUi;

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

        ShowStatus(
            "Global shortcut unavailable",
            $"Win + Shift + A could not be registered (Windows error {errorCode}).",
            InfoBarSeverity.Warning);
    }

    internal void DisposeWorkspace()
    {
        _workspaceCoordinator?.Dispose();
        _workspaceCoordinator = null;
    }

    internal void SetWindowVisibility(bool isVisible) => _workspaceCoordinator?.SetWindowVisibility(isVisible);

    internal void OpenSettings()
    {
        ConfigureSettingsUi();
        SettingsOverlay.Visibility = Visibility.Visible;
        SettingsCloseButton.Focus(FocusState.Programmatic);
    }

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_hasLoaded)
        {
            return;
        }

        _hasLoaded = true;
        _settings = await _sessionStore.LoadSettingsAsync();
        if (_settings.FirstUsedUtc is null)
        {
            _settings = _settings with { FirstUsedUtc = DateTimeOffset.UtcNow };
            await PersistSettingsAsync();
        }

        _workspaceCoordinator = new WorkspaceCoordinator(WebViewHost, RequestPermissionAsync, _settings.MemoryMode);
        _workspaceCoordinator.StateChanged += Workspace_StateChanged;
        _workspaceCoordinator.RestoreLocatorChanged += Workspace_RestoreLocatorChanged;
        _workspaceCoordinator.LifecycleChanged += Workspace_LifecycleChanged;
        _workspaceCoordinator.SuccessfulOpen += Workspace_SuccessfulOpen;
        PopulateProviderChooser();
        await RestoreSessionAsync();
        ConfigureSettingsUi();

        if (_settings.OnboardingVersion < 1)
        {
            await ShowWelcomeAsync();
            _settings = _settings with { OnboardingVersion = 1 };
            await PersistSettingsAsync();
        }

        var targetWorkspaceId = _activeWorkspace?.Id ?? _workspaces.First().Id;
        await SelectWorkspaceAsync(targetWorkspaceId);
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

        if (args.Activity != WorkspaceActivity.None)
        {
            ShowWorkspaceActivity(args.Activity, args.Title);
            StatusBanner.Visibility = Visibility.Collapsed;
            RecoveryPanel.Visibility = Visibility.Collapsed;
            return;
        }

        HideWorkspaceActivity();

        if (args.Severity == InfoBarSeverity.Success)
        {
            StatusBanner.Visibility = Visibility.Collapsed;
            RecoveryPanel.Visibility = Visibility.Collapsed;
            return;
        }

        ShowStatus(args.Title, args.Message, args.Severity);

        RecoveryTitle.Text = args.Title;
        RecoveryMessage.Text = args.Message;
        RecoveryPanel.Visibility = args.RequiresRecovery ? Visibility.Visible : Visibility.Collapsed;
        if (args.RequiresRecovery)
        {
            AnimateIn(RecoveryPanel);
        }
    }

    private async void Workspace_RestoreLocatorChanged(object? sender, RestoreLocatorChangedEventArgs args)
    {
        if (_workspaces.FirstOrDefault(workspace => workspace.Id == args.WorkspaceId) is not { } workspace)
        {
            return;
        }

        workspace.SetRestoreLocator(_settings.RestoreExactWorkspace ? args.RestoreLocator : null);
        await PersistSessionAsync();
    }

    private void Workspace_LifecycleChanged(object? sender, WorkspaceLifecycleChangedEventArgs args)
    {
        if (_workspaces.FirstOrDefault(workspace => workspace.Id == args.WorkspaceId) is not { } workspace)
        {
            return;
        }

        workspace.SetLifecyclePhase(args.Phase);
        UpdateWorkspaceTab(workspace);
    }

    private async void Workspace_SuccessfulOpen(object? sender, string workspaceId)
    {
        _settings = _settings with { SuccessfulOpenCount = _settings.SuccessfulOpenCount + 1 };
        await PersistSettingsAsync();
        UpdateSupportReminderVisibility();
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        WorkspaceActionsFlyout.Hide();
        RecoveryPanel.Visibility = Visibility.Collapsed;
        _workspaceCoordinator?.ReloadActiveWorkspace();
    }

    private async void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        WorkspaceActionsFlyout.Hide();
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
            || _workspaces.FirstOrDefault(workspace => workspace.Id == workspaceId) is not { } workspace
            || !_workspaceTabViews.TryGetValue(workspace.Id, out var tabView)
            || !tabView.Close.IsEnabled)
        {
            return;
        }

        tabView.Close.IsEnabled = false;
        await AnimateOutAsync(tabView.Container);
        var wasActive = ReferenceEquals(workspace, _activeWorkspace);
        if (_workspaceCoordinator is not null)
        {
            await _workspaceCoordinator.RemoveWorkspaceAsync(workspace.Id);
        }
        _workspaces.Remove(workspace);

        if (_workspaceTabViews.Remove(workspace.Id))
        {
            WorkspaceTabs.Children.Remove(tabView.Container);
        }

        UpdateCloseButtonVisibility();
        await PersistSessionAsync();
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
        WorkspaceActionsFlyout.Hide();
        if (_workspaceCoordinator?.ActiveWorkspace is not { } workspace)
        {
            return;
        }

        var confirmation = await ShowPromptAsync(
            $"Reset {workspace.Provider.DisplayName} website data?",
            $"This clears the local cookies, cache, site storage, and remembered permissions shared by {_workspaces.Count(candidate => candidate.Provider?.Id == workspace.Provider.Id)} AI Drawer workspace(s) using this provider. Those workspaces may need to reload. Your provider account and provider-hosted conversations are not deleted.",
            "Reset website data",
            "Cancel");

        if (!confirmation.IsPrimary)
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
        var workspace = new WorkspaceTab(GetNextHomeWorkspaceNumber());
        _workspaces.Add(workspace);
        AddWorkspaceTab(workspace);
        UpdateCloseButtonVisibility();
        await PersistSessionAsync();
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
        await PersistSessionAsync();
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
        HideWorkspaceActivity();
        RecoveryPanel.Visibility = Visibility.Collapsed;

        if (workspace.IsHome)
        {
            _workspaceCoordinator.DeactivateActiveWorkspace();
            HomePanel.Visibility = Visibility.Visible;
            WebViewHost.Visibility = Visibility.Collapsed;
            AnimateIn(HomePanel);
            CompatibilityStatusText.Visibility = Visibility.Collapsed;
            WorkspaceActionsButton.IsEnabled = false;
            StatusBanner.Visibility = Visibility.Collapsed;
            UpdateSupportReminderVisibility();
            await PersistSessionAsync();
            return;
        }

        if (workspace.IsProviderUnavailable)
        {
            _workspaceCoordinator.DeactivateActiveWorkspace();
            HomePanel.Visibility = Visibility.Collapsed;
            WebViewHost.Visibility = Visibility.Collapsed;
            CompatibilityStatusText.Visibility = Visibility.Collapsed;
            WorkspaceActionsButton.IsEnabled = false;
            HomeSupportReminder.Visibility = Visibility.Collapsed;
            ShowStatus(
                "Workspace provider unavailable",
                $"AI Drawer preserved this workspace, but its provider definition '{workspace.ProviderId}' is not available in this build.",
                InfoBarSeverity.Warning);
            await PersistSessionAsync();
            return;
        }

        HomePanel.Visibility = Visibility.Collapsed;
        WebViewHost.Visibility = Visibility.Visible;
        AnimateIn(WebViewHost);
        var provider = workspace.Provider ?? throw new InvalidOperationException("A non-home workspace must have a provider.");
        CompatibilityStatusText.Text = provider.CompatibilityStatus;
        CompatibilityStatusText.Visibility = Visibility.Visible;
        ReloadActionText.Text = $"Reload {provider.DisplayName}";
        RestartActionText.Text = $"Restart {provider.DisplayName} workspace";
        WorkspaceActionsButton.IsEnabled = true;
        _updatingSettingsUi = true;
        KeepActiveToggle.IsOn = workspace.KeepActive;
        _updatingSettingsUi = false;
        HomeSupportReminder.Visibility = Visibility.Collapsed;
        await _workspaceCoordinator.ActivateAsync(
            workspace.Id,
            provider,
            _settings.RestoreExactWorkspace ? workspace.RestoreLocator : null,
            workspace.KeepActive,
            _settings.RestoreExactWorkspace && workspace.WasRestoredFromSession);
        await PersistSessionAsync();
    }

    private void PopulateProviderChooser()
    {
        if (_workspaceCoordinator is null)
        {
            return;
        }

        ProviderChooser.Children.Clear();
        ProviderChooser.ColumnDefinitions.Clear();
        ProviderChooser.RowDefinitions.Clear();
        ProviderChooser.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ProviderChooser.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rowCount = (_workspaceCoordinator.Providers.Count + 1) / 2;
        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            ProviderChooser.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (var providerIndex = 0; providerIndex < _workspaceCoordinator.Providers.Count; providerIndex++)
        {
            var provider = _workspaceCoordinator.Providers[providerIndex];
            var rowContent = new Grid
            {
                ColumnSpacing = 10,
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(32) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto }
                }
            };

            rowContent.Children.Add(CreateProviderMark(provider));

            var providerText = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 0,
                Children =
                {
                    new TextBlock
                    {
                        Text = provider.DisplayName,
                        FontSize = 14,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    },
                    new TextBlock
                    {
                        Text = provider.CompatibilityStatus,
                        FontSize = 11,
                        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                    }
                }
            };
            Grid.SetColumn(providerText, 1);
            rowContent.Children.Add(providerText);

            var shortcut = new TextBlock
            {
                Text = $"Ctrl+{providerIndex + 1}",
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
                Opacity = 0.72
            };
            Grid.SetColumn(shortcut, 2);
            rowContent.Children.Add(shortcut);

            var row = new Button
            {
                Tag = provider.Id,
                Style = (Style)Resources["ProviderChoiceButtonStyle"],
                Content = rowContent
            };
            AutomationProperties.SetName(row, $"Open {provider.DisplayName} workspace");
            row.Click += ProviderChoice_Click;

            Grid.SetRow(row, providerIndex / 2);
            Grid.SetColumn(row, providerIndex % 2);
            ProviderChooser.Children.Add(row);
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

    private int GetNextHomeWorkspaceNumber()
    {
        var workspaceNumber = 1;
        while (_workspaces.Any(workspace =>
                   workspace.IsHome
                   && string.Equals(
                       workspace.DisplayName,
                       workspaceNumber == 1 ? "New workspace" : $"New workspace {workspaceNumber}",
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
        AutomationProperties.SetName(close, $"Close {workspace.DisplayName}");
        close.Click += CloseWorkspaceTab_Click;

        container.Children.Add(tab);
        container.Children.Add(close);
        WorkspaceTabs.Children.Add(container);
        _workspaceTabViews.Add(workspace.Id, new WorkspaceTabView(container, tab, close));
        AnimateIn(container);
    }

    private void UpdateWorkspaceTab(WorkspaceTab workspace)
    {
        if (_workspaceTabViews.TryGetValue(workspace.Id, out var tabView))
        {
            tabView.Tab.Content = workspace.LifecyclePhase switch
            {
                WorkspaceLifecyclePhase.Recent => $"{workspace.DisplayName} · recent",
                WorkspaceLifecyclePhase.Disposed => $"{workspace.DisplayName} · reload",
                _ => workspace.DisplayName
            };
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
        var result = await ShowPromptAsync(
            $"Allow {request.PermissionKind} for {request.ProviderName}?",
            "The provider requested a privileged browser permission. AI Drawer will not grant it automatically.",
            "Allow",
            "Deny",
            showRememberChoice: true);

        return new PermissionDecision(result.IsPrimary, result.RememberDecision);
    }

    private async Task RestoreSessionAsync()
    {
        if (_workspaceCoordinator is null)
        {
            return;
        }

        var session = await _sessionStore.LoadSessionAsync();
        foreach (var restored in session.Workspaces)
        {
            var provider = restored.ProviderId is null
                ? null
                : _workspaceCoordinator.Providers.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, restored.ProviderId, StringComparison.Ordinal));
            var locator = _settings.RestoreExactWorkspace
                ? provider?.CreateRestoreLocator(restored.RestoreLocator)
                : null;
            var workspace = new WorkspaceTab(
                restored.Id,
                restored.DisplayName,
                provider,
                restored.ProviderId,
                restored.KeepActive,
                locator);
            _workspaces.Add(workspace);
            AddWorkspaceTab(workspace);
        }

        if (_workspaces.Count == 0)
        {
            var workspace = new WorkspaceTab(1);
            _workspaces.Add(workspace);
            AddWorkspaceTab(workspace);
        }

        UpdateCloseButtonVisibility();
        _activeWorkspace = _workspaces.FirstOrDefault(workspace =>
            string.Equals(workspace.Id, session.ActiveWorkspaceId, StringComparison.Ordinal))
            ?? _workspaces[0];
    }

    private async Task PersistSessionAsync()
    {
        try
        {
            await _sessionStore.SaveSessionAsync(
                _workspaces,
                _activeWorkspace?.Id,
                _settings.RestoreExactWorkspace);
        }
        catch (Exception exception)
        {
            ShowStatus(
                "Workspace changes could not be saved",
                $"The current session is still available, but restart restore may be incomplete ({exception.GetType().Name}).",
                InfoBarSeverity.Warning);
        }
    }

    private async Task PersistSettingsAsync()
    {
        try
        {
            await _sessionStore.SaveSettingsAsync(_settings);
        }
        catch (Exception exception)
        {
            ShowStatus(
                "Settings could not be saved",
                $"The change applies for this session but may not survive restart ({exception.GetType().Name}).",
                InfoBarSeverity.Warning);
        }
    }

    private async Task ShowWelcomeAsync()
    {
        await ShowPromptAsync(
            "Welcome to AI Drawer",
            "AI Drawer opens official AI websites in private local provider profiles. Multiple native workspaces can share one provider sign-in. To limit memory, an inactive WebView may be released while its tab remains; exact restore is limited to reviewed provider URL patterns. AI Drawer never reads or stores prompts, responses, page content, credentials, cookies, tokens, or payment data.",
            "Continue",
            "Skip");
    }

    private void ConfigureSettingsUi()
    {
        _updatingSettingsUi = true;
        RestoreExactWorkspaceToggle.IsOn = _settings.RestoreExactWorkspace;
        MemoryModeComboBox.SelectedIndex = _settings.MemoryMode switch
        {
            MemoryMode.LowMemory => 0,
            MemoryMode.FastSwitching => 2,
            _ => 1
        };
        SupportDevelopmentButton.IsEnabled = true;
        SupportLinkStatusText.Text =
            "Opens a shared Buy Me a Coffee page in your browser. Contributions do not unlock AI Drawer or activate any application, AI provider, subscription, account, premium feature, or support plan.";
        _updatingSettingsUi = false;
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
        => OpenSettings();

    private void CloseSettingsButton_Click(object sender, RoutedEventArgs e) =>
        SettingsOverlay.Visibility = Visibility.Collapsed;

    private async void ShowWelcomeButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsOverlay.Visibility = Visibility.Collapsed;
        await ShowWelcomeAsync();
    }

    private async void RestoreExactWorkspaceToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updatingSettingsUi)
        {
            return;
        }

        _settings = _settings with { RestoreExactWorkspace = RestoreExactWorkspaceToggle.IsOn };
        if (!_settings.RestoreExactWorkspace)
        {
            _workspaceCoordinator?.ClearAllRestoreLocators();
            foreach (var workspace in _workspaces)
            {
                workspace.SetRestoreLocator(null);
            }
        }

        await PersistSettingsAsync();
        await PersistSessionAsync();
    }

    private async void MemoryModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingSettingsUi || MemoryModeComboBox.SelectedItem is not ComboBoxItem { Tag: string tag })
        {
            return;
        }

        _settings = _settings with
        {
            MemoryMode = Enum.TryParse<MemoryMode>(tag, out var mode) ? mode : MemoryMode.Balanced
        };
        _workspaceCoordinator?.SetMemoryMode(_settings.MemoryMode);
        await PersistSettingsAsync();
    }

    private async void KeepActiveToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updatingSettingsUi || _activeWorkspace is null)
        {
            return;
        }

        _activeWorkspace.SetKeepActive(KeepActiveToggle.IsOn);
        _workspaceCoordinator?.SetActiveWorkspaceKeepActive(KeepActiveToggle.IsOn);
        await PersistSessionAsync();
    }

    private void UpdateSupportReminderVisibility()
    {
        if (_activeWorkspace?.IsHome != true
            || _settings.SupportReminderDismissed
            || _settings.SupportReminderSnoozedUntilUtc > DateTimeOffset.UtcNow
            || _settings.FirstUsedUtc is not { } firstUsed)
        {
            HomeSupportReminder.Visibility = Visibility.Collapsed;
            return;
        }

        var age = DateTimeOffset.UtcNow - firstUsed;
        var eligible = age >= TimeSpan.FromDays(7)
            && (age >= TimeSpan.FromDays(14) || _settings.SuccessfulOpenCount >= 20);
        HomeSupportReminder.Visibility = eligible ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void SupportDevelopmentButton_Click(object sender, RoutedEventArgs e)
    {
        await Launcher.LaunchUriAsync(AppLinks.BuyMeACoffeeUri);
    }

    private async void SupportNotNowButton_Click(object sender, RoutedEventArgs e)
    {
        _settings = _settings with
        {
            SupportReminderSnoozedUntilUtc = DateTimeOffset.UtcNow.AddDays(90)
        };
        HomeSupportReminder.Visibility = Visibility.Collapsed;
        await PersistSettingsAsync();
    }

    private async void SupportNeverButton_Click(object sender, RoutedEventArgs e)
    {
        _settings = _settings with { SupportReminderDismissed = true };
        HomeSupportReminder.Visibility = Visibility.Collapsed;
        await PersistSettingsAsync();
    }

    private void ShowStatus(string title, string message, InfoBarSeverity severity)
    {
        StatusTitle.Text = title;
        StatusMessage.Text = message;
        StatusBanner.Background = (Brush)Resources[severity switch
        {
            InfoBarSeverity.Warning => "StatusWarningBrush",
            InfoBarSeverity.Error => "StatusErrorBrush",
            _ => "StatusInfoBrush"
        }];
        StatusGlyph.Glyph = severity switch
        {
            InfoBarSeverity.Warning => "\uE7BA",
            InfoBarSeverity.Error => "\uEA39",
            _ => "\uE946"
        };
        StatusBanner.Visibility = Visibility.Visible;
        AnimateIn(StatusBanner);
    }

    private void ShowWorkspaceActivity(WorkspaceActivity activity, string title)
    {
        if (activity == WorkspaceActivity.Opening)
        {
            StopNavigationActivityAnimation();
            WorkspaceLoadingTitle.Text = title;
            WorkspaceLoadingPanel.Visibility = Visibility.Visible;
            AnimateIn(WorkspaceLoadingPanel);
            StartWorkspaceLoadingAnimation();
            return;
        }

        WorkspaceLoadingPanel.Visibility = Visibility.Collapsed;
        StopWorkspaceLoadingAnimation();
        StartNavigationActivityAnimation();
    }

    private void HideWorkspaceActivity()
    {
        WorkspaceLoadingPanel.Visibility = Visibility.Collapsed;
        StopWorkspaceLoadingAnimation();
        StopNavigationActivityAnimation();
    }

    private void StartWorkspaceLoadingAnimation()
    {
        var visual = ElementCompositionPreview.GetElementVisual(WorkspaceLoadingMark);
        visual.StopAnimation(nameof(Visual.Scale));
        visual.StopAnimation(nameof(Visual.Opacity));
        visual.CenterPoint = new Vector3(21, 21, 0);

        if (!_uiSettings.AnimationsEnabled)
        {
            visual.Scale = Vector3.One;
            visual.Opacity = 1;
            return;
        }

        var scale = visual.Compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0, new Vector3(0.94f, 0.94f, 1));
        scale.InsertKeyFrame(0.5f, new Vector3(1.08f, 1.08f, 1));
        scale.InsertKeyFrame(1, new Vector3(0.94f, 0.94f, 1));
        scale.Duration = TimeSpan.FromMilliseconds(1400);
        scale.IterationBehavior = AnimationIterationBehavior.Forever;

        var opacity = visual.Compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(0, 0.72f);
        opacity.InsertKeyFrame(0.5f, 1);
        opacity.InsertKeyFrame(1, 0.72f);
        opacity.Duration = scale.Duration;
        opacity.IterationBehavior = AnimationIterationBehavior.Forever;

        visual.StartAnimation(nameof(Visual.Scale), scale);
        visual.StartAnimation(nameof(Visual.Opacity), opacity);
    }

    private void StopWorkspaceLoadingAnimation()
    {
        var visual = ElementCompositionPreview.GetElementVisual(WorkspaceLoadingMark);
        visual.StopAnimation(nameof(Visual.Scale));
        visual.StopAnimation(nameof(Visual.Opacity));
        visual.Scale = Vector3.One;
        visual.Opacity = 1;
    }

    private void StartNavigationActivityAnimation()
    {
        var visual = ElementCompositionPreview.GetElementVisual(NavigationActivityBar);
        visual.StopAnimation(nameof(Visual.Opacity));

        if (!_uiSettings.AnimationsEnabled)
        {
            visual.Opacity = 0.8f;
            return;
        }

        var pulse = visual.Compositor.CreateScalarKeyFrameAnimation();
        pulse.InsertKeyFrame(0, 0.22f);
        pulse.InsertKeyFrame(0.5f, 0.95f);
        pulse.InsertKeyFrame(1, 0.22f);
        pulse.Duration = TimeSpan.FromMilliseconds(900);
        pulse.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation(nameof(Visual.Opacity), pulse);
    }

    private void StopNavigationActivityAnimation()
    {
        var visual = ElementCompositionPreview.GetElementVisual(NavigationActivityBar);
        visual.StopAnimation(nameof(Visual.Opacity));
        visual.Opacity = 0;
        NavigationActivityBar.Opacity = 0;
    }

    private Task<PromptDecision> ShowPromptAsync(
        string title,
        string message,
        string primaryButtonText,
        string secondaryButtonText,
        bool showRememberChoice = false)
    {
        if (_promptCompletion is not null)
        {
            return Task.FromResult(new PromptDecision(false, false));
        }

        PromptTitle.Text = title;
        PromptMessage.Text = message;
        PromptPrimaryButton.Content = primaryButtonText;
        PromptSecondaryButton.Content = secondaryButtonText;
        PromptRememberCheckBox.IsChecked = false;
        PromptRememberCheckBox.Visibility = showRememberChoice ? Visibility.Visible : Visibility.Collapsed;
        _promptCompletion = new TaskCompletionSource<PromptDecision>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        PromptOverlay.Visibility = Visibility.Visible;
        AnimateIn(PromptCard);
        _ = DispatcherQueue.TryEnqueue(() => PromptSecondaryButton.Focus(FocusState.Programmatic));

        return _promptCompletion.Task;
    }

    private void PromptPrimaryButton_Click(object sender, RoutedEventArgs e) => CompletePrompt(true);

    private void PromptSecondaryButton_Click(object sender, RoutedEventArgs e) => CompletePrompt(false);

    private void PromptCancelShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_promptCompletion is null)
        {
            if (SettingsOverlay.Visibility == Visibility.Visible)
            {
                args.Handled = true;
                SettingsOverlay.Visibility = Visibility.Collapsed;
            }

            return;
        }

        args.Handled = true;
        CompletePrompt(false);
    }

    private void CompletePrompt(bool isPrimary)
    {
        var completion = _promptCompletion;
        if (completion is null)
        {
            return;
        }

        var rememberDecision = PromptRememberCheckBox.Visibility == Visibility.Visible
            && PromptRememberCheckBox.IsChecked == true;
        _promptCompletion = null;
        PromptOverlay.Visibility = Visibility.Collapsed;
        completion.TrySetResult(new PromptDecision(isPrimary, rememberDecision));
    }

    private void AnimateIn(UIElement element)
    {
        if (!_uiSettings.AnimationsEnabled)
        {
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        visual.StopAnimation(nameof(Visual.Opacity));
        visual.Opacity = 0;

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, 1);
        fade.Duration = TimeSpan.FromMilliseconds(180);

        visual.StartAnimation(nameof(Visual.Opacity), fade);
    }

    private Task AnimateOutAsync(UIElement element)
    {
        if (!_uiSettings.AnimationsEnabled)
        {
            return Task.CompletedTask;
        }

        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        var completion = new TaskCompletionSource<bool>();
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

        visual.StopAnimation(nameof(Visual.Opacity));
        visual.Opacity = 1;

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, 0);
        fade.Duration = TimeSpan.FromMilliseconds(130);

        visual.StartAnimation(nameof(Visual.Opacity), fade);
        batch.End();
        batch.Completed += (_, _) =>
        {
            visual.StopAnimation(nameof(Visual.Opacity));
            visual.Opacity = 1;
            completion.TrySetResult(true);
        };

        return completion.Task;
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        WorkspaceActionsFlyout.Hide();
        App.ExitApplication();
    }

    private sealed record WorkspaceTabView(Grid Container, Button Tab, Button Close);
    private sealed record PromptDecision(bool IsPrimary, bool RememberDecision);
}
