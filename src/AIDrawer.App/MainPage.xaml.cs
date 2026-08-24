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
    private static readonly Uri SupportUri = new("https://buymeacoffee.com/edward_lee");
    private const int CurrentOnboardingVersion = 2;
    private readonly Dictionary<string, WorkspaceTabView> _workspaceTabViews = new(StringComparer.Ordinal);
    private readonly HashSet<string> _closingWorkspaceIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _externalPromptUris = new(StringComparer.Ordinal);
    private readonly Queue<NavigationPromptRequestedEventArgs> _navigationPrompts = new();
    private readonly HashSet<string> _purchasePromptWorkspaceIds = new(StringComparer.Ordinal);
    private readonly List<WorkspaceTab> _workspaces = [];
    private readonly UISettings _uiSettings = new();
    private readonly WorkspaceSessionStore _sessionStore = new();
    private WorkspaceCoordinator? _workspaceCoordinator;
    private WorkspaceTab? _activeWorkspace;
    private AppSettings _settings = new();
    private TaskCompletionSource<PromptDecision>? _promptCompletion;
    private TaskCompletionSource<SessionRecoveryDecision>? _sessionRecoveryCompletion;
    private PageLifecycleState _pageState;
    private long _selectionVersion;
    private string? _providerResetInProgress;
    private bool _updatingSettingsUi;
    private bool _processingNavigationPrompts;

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
        if (_workspaceCoordinator is not { } coordinator)
        {
            return;
        }

        coordinator.StateChanged -= Workspace_StateChanged;
        coordinator.RestoreLocatorChanged -= Workspace_RestoreLocatorChanged;
        coordinator.LifecycleChanged -= Workspace_LifecycleChanged;
        coordinator.SuccessfulOpen -= Workspace_SuccessfulOpen;
        coordinator.NavigationPromptRequested -= Workspace_NavigationPromptRequested;
        coordinator.Dispose();
        _workspaceCoordinator = null;
    }

    internal async Task PersistAndDisposeWorkspaceAsync()
    {
        var shouldPersist = _pageState == PageLifecycleState.Ready;
        _pageState = PageLifecycleState.ShuttingDown;
        _selectionVersion++;
        CompletePrompt(isPrimary: false);
        CompleteSessionRecovery(SessionRecoveryDecision.Exit);
        _navigationPrompts.Clear();
        _externalPromptUris.Clear();
        _purchasePromptWorkspaceIds.Clear();

        if (shouldPersist && _settings.RestoreExactWorkspace && _workspaceCoordinator is { } coordinator)
        {
            PromoteCommittedRestoreLocators(coordinator);
        }

        DisposeWorkspace();
        if (shouldPersist)
        {
            await PersistSessionAsync();
            await PersistSettingsAsync();
        }
        else
        {
            try
            {
                await WorkspaceSessionStore.FlushWritesAsync();
            }
            catch
            {
                // The initiating load-time save owns its error; Exit only waits for it to finish.
            }
        }
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
        if (_pageState != PageLifecycleState.Unloaded)
        {
            return;
        }

        _pageState = PageLifecycleState.Loading;
        _settings = await _sessionStore.LoadSettingsAsync();
        if (_pageState != PageLifecycleState.Loading)
        {
            return;
        }

        if (_settings.FirstUsedUtc is null)
        {
            _settings = _settings with { FirstUsedUtc = DateTimeOffset.UtcNow };
            await PersistSettingsAsync();
            if (_pageState != PageLifecycleState.Loading)
            {
                return;
            }
        }

        _workspaceCoordinator = new WorkspaceCoordinator(WebViewHost, RequestPermissionAsync, _settings.MemoryMode);
        _workspaceCoordinator.StateChanged += Workspace_StateChanged;
        _workspaceCoordinator.RestoreLocatorChanged += Workspace_RestoreLocatorChanged;
        _workspaceCoordinator.LifecycleChanged += Workspace_LifecycleChanged;
        _workspaceCoordinator.SuccessfulOpen += Workspace_SuccessfulOpen;
        _workspaceCoordinator.NavigationPromptRequested += Workspace_NavigationPromptRequested;
        PopulateProviderChooser();
        if (!await RestoreSessionWithRecoveryAsync()
            || _pageState != PageLifecycleState.Loading)
        {
            return;
        }

        ConfigureSettingsUi();

        if (_settings.OnboardingVersion < CurrentOnboardingVersion)
        {
            await ShowWelcomeAsync(_settings.OnboardingVersion == 0
                ? WelcomeDisclosureMode.Full
                : WelcomeDisclosureMode.UpdatedPrivacyBoundary);
            if (_pageState != PageLifecycleState.Loading)
            {
                return;
            }

            _settings = _settings with { OnboardingVersion = CurrentOnboardingVersion };
            await PersistSettingsAsync();
            if (_pageState != PageLifecycleState.Loading)
            {
                return;
            }
        }

        var targetWorkspaceId = _activeWorkspace?.Id ?? _workspaces.First().Id;
        _pageState = PageLifecycleState.Ready;
        await SelectWorkspaceAsync(targetWorkspaceId);
    }

    private void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_pageState != PageLifecycleState.Unloaded)
        {
            _pageState = PageLifecycleState.ShuttingDown;
            _selectionVersion++;
            CompletePrompt(isPrimary: false);
            CompleteSessionRecovery(SessionRecoveryDecision.Exit);
            _navigationPrompts.Clear();
            _externalPromptUris.Clear();
            _purchasePromptWorkspaceIds.Clear();
            DisposeWorkspace();
            _workspaces.Clear();
            _closingWorkspaceIds.Clear();
            _workspaceTabViews.Clear();
            WorkspaceTabs.Children.Clear();
            _activeWorkspace = null;
            _pageState = PageLifecycleState.Unloaded;
        }
    }

    private void Workspace_StateChanged(object? sender, WorkspaceStateChangedEventArgs args)
    {
        if (_pageState != PageLifecycleState.Ready
            || !string.Equals(_activeWorkspace?.Id, args.WorkspaceId, StringComparison.Ordinal))
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
        if (_pageState != PageLifecycleState.Ready
            || !_settings.RestoreExactWorkspace
            || _closingWorkspaceIds.Contains(args.WorkspaceId)
            || _workspaces.FirstOrDefault(workspace => workspace.Id == args.WorkspaceId) is not { } workspace
            || string.Equals(workspace.ProviderId, _providerResetInProgress, StringComparison.Ordinal))
        {
            return;
        }

        workspace.SetRestoreLocator(args.RestoreLocator);
        await PersistSessionAsync();
    }

    private void Workspace_LifecycleChanged(object? sender, WorkspaceLifecycleChangedEventArgs args)
    {
        if (_pageState != PageLifecycleState.Ready
            || _workspaces.FirstOrDefault(workspace => workspace.Id == args.WorkspaceId) is not { } workspace)
        {
            return;
        }

        workspace.SetLifecyclePhase(args.Phase);
        UpdateWorkspaceTab(workspace);
        if (args.Phase == WorkspaceLifecyclePhase.Active
            && string.Equals(_activeWorkspace?.Id, args.WorkspaceId, StringComparison.Ordinal))
        {
            WorkspaceActionsButton.IsEnabled = true;
        }
    }

    private async void Workspace_NavigationPromptRequested(object? sender, NavigationPromptRequestedEventArgs args)
    {
        if (_pageState != PageLifecycleState.Ready
            || !string.Equals(_activeWorkspace?.Id, args.WorkspaceId, StringComparison.Ordinal)
            || args.Kind == NavigationPromptKind.ExternalLink && args.ExternalUri is null)
        {
            return;
        }

        if (args.Kind == NavigationPromptKind.PurchaseBlocked
            && !_purchasePromptWorkspaceIds.Add(args.WorkspaceId))
        {
            return;
        }

        if (args.Kind == NavigationPromptKind.ExternalLink
            && !_externalPromptUris.Add(args.ExternalUri!.AbsoluteUri))
        {
            return;
        }

        _navigationPrompts.Enqueue(args);
        await ProcessNavigationPromptsAsync();
    }

    private async Task ProcessNavigationPromptsAsync()
    {
        if (_processingNavigationPrompts)
        {
            return;
        }

        _processingNavigationPrompts = true;
        try
        {
            while (_pageState == PageLifecycleState.Ready && _navigationPrompts.TryDequeue(out var request))
            {
                try
                {
                    while (_pageState == PageLifecycleState.Ready && _promptCompletion is not null)
                    {
                        await Task.Delay(50);
                    }

                    if (_pageState != PageLifecycleState.Ready
                        || !string.Equals(_activeWorkspace?.Id, request.WorkspaceId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (request.Kind == NavigationPromptKind.PurchaseBlocked)
                    {
                        await ShowPromptAsync(
                            "Purchase stays outside AI Drawer",
                            "AI Drawer does not provide or process subscriptions, billing, cancellations, refunds, or payment information. To reduce embedded-payment risk, use Edge, Chrome, or another browser you trust to visit the provider's official website yourself.",
                            "Got it",
                            secondaryButtonText: null);
                        continue;
                    }

                    if (request.ExternalUri is not { } externalUri)
                    {
                        continue;
                    }

                    var decision = await ShowPromptAsync(
                        "Open external link in your browser?",
                        "This destination is outside the reviewed provider and authentication origins. AI Drawer removed query parameters and fragments before this optional browser handoff.",
                        "Open in browser",
                        "Stay in AI Drawer");
                    if (decision.IsPrimary
                        && _pageState == PageLifecycleState.Ready
                        && string.Equals(_activeWorkspace?.Id, request.WorkspaceId, StringComparison.Ordinal))
                    {
                        await LaunchExternalUriAsync(externalUri);
                    }
                }
                finally
                {
                    if (request.Kind == NavigationPromptKind.PurchaseBlocked)
                    {
                        _purchasePromptWorkspaceIds.Remove(request.WorkspaceId);
                    }
                    else if (request.ExternalUri is not null)
                    {
                        _externalPromptUris.Remove(request.ExternalUri.AbsoluteUri);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            if (_pageState == PageLifecycleState.Ready)
            {
                ShowStatus(
                    "Navigation confirmation could not be shown",
                    $"The blocked navigation was kept outside AI Drawer ({exception.GetType().Name}).",
                    InfoBarSeverity.Warning);
            }
        }
        finally
        {
            _processingNavigationPrompts = false;
        }
    }

    private async Task LaunchExternalUriAsync(Uri uri)
    {
        try
        {
            if (!await Launcher.LaunchUriAsync(uri))
            {
                ShowStatus(
                    "Link could not be opened",
                    "Windows did not find an application that could open this HTTPS link.",
                    InfoBarSeverity.Warning);
            }
        }
        catch (Exception exception)
        {
            ShowStatus(
                "Link could not be opened",
                $"Windows could not open this HTTPS link ({exception.GetType().Name}).",
                InfoBarSeverity.Warning);
        }
    }

    private async void Workspace_SuccessfulOpen(object? sender, string workspaceId)
    {
        if (_pageState != PageLifecycleState.Ready)
        {
            return;
        }

        _settings = _settings with
        {
            SuccessfulOpenCount = _settings.SuccessfulOpenCount == int.MaxValue
                ? int.MaxValue
                : _settings.SuccessfulOpenCount + 1
        };
        await PersistSettingsAsync();
        UpdateSupportReminderVisibility();
    }

    private async void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        WorkspaceActionsFlyout.Hide();
        RecoveryPanel.Visibility = Visibility.Collapsed;
        if (_pageState != PageLifecycleState.Ready
            || _providerResetInProgress is not null
            || _activeWorkspace is not { } workspace
            || _workspaceCoordinator is not { } coordinator)
        {
            return;
        }

        if (!coordinator.ReloadActiveWorkspace(workspace.Id))
        {
            await RestartActiveWorkspaceFromUiAsync(coordinator);
        }
    }

    private async void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        WorkspaceActionsFlyout.Hide();
        RecoveryPanel.Visibility = Visibility.Collapsed;
        if (_workspaceCoordinator is { } coordinator)
        {
            await RestartActiveWorkspaceFromUiAsync(coordinator);
        }
    }

    private async Task RestartActiveWorkspaceFromUiAsync(WorkspaceCoordinator coordinator)
    {
        if (_pageState != PageLifecycleState.Ready
            || _providerResetInProgress is not null
            || _activeWorkspace is not { } workspace)
        {
            return;
        }

        var selectionVersion = _selectionVersion;
        WorkspaceActionsButton.IsEnabled = false;
        var restarted = await coordinator.RestartActiveWorkspaceAsync(workspace.Id);
        if (IsCurrentSelection(selectionVersion, workspace))
        {
            WorkspaceActionsButton.IsEnabled = restarted;
        }
    }

    private async void NewWorkspaceButton_Click(object sender, RoutedEventArgs e) => await CreateWorkspaceAsync();

    private async void NewWorkspaceShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (_promptCompletion is not null)
        {
            return;
        }

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
        if (_pageState != PageLifecycleState.Ready
            || _providerResetInProgress is not null
            || sender is not Button { Tag: string workspaceId }
            || _workspaces.Count - _closingWorkspaceIds.Count <= 1
            || _workspaces.FirstOrDefault(workspace => workspace.Id == workspaceId) is not { } workspace
            || !_workspaceTabViews.TryGetValue(workspace.Id, out var tabView)
            || !tabView.Close.IsEnabled
            || !_closingWorkspaceIds.Add(workspace.Id))
        {
            return;
        }

        tabView.Close.IsEnabled = false;
        tabView.Tab.IsEnabled = false;
        if (ReferenceEquals(workspace, _activeWorkspace))
        {
            _selectionVersion++;
        }

        await AnimateOutAsync(tabView.Container);
        if (_pageState != PageLifecycleState.Ready)
        {
            return;
        }

        if (_workspaceCoordinator is not null)
        {
            await _workspaceCoordinator.RemoveWorkspaceAsync(workspace.Id);
            if (_pageState != PageLifecycleState.Ready)
            {
                return;
            }
        }
        _workspaces.Remove(workspace);

        if (_workspaceTabViews.Remove(workspace.Id))
        {
            WorkspaceTabs.Children.Remove(tabView.Container);
        }

        _closingWorkspaceIds.Remove(workspace.Id);
        UpdateCloseButtonVisibility();
        await PersistSessionAsync();
        if (ReferenceEquals(workspace, _activeWorkspace))
        {
            var fallback = _workspaces.Last(candidate => !_closingWorkspaceIds.Contains(candidate.Id));
            await SelectWorkspaceAsync(fallback.Id);
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
        if (_promptCompletion is not null)
        {
            args.Handled = true;
            return;
        }

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

        if (await CreateWorkspaceAsync() is { } newWorkspace)
        {
            await OpenProviderInWorkspaceAsync(newWorkspace, provider.Id);
        }
    }

    private async void ResetWebsiteDataMenuItem_Click(object sender, RoutedEventArgs e)
    {
        WorkspaceActionsFlyout.Hide();
        var coordinator = _workspaceCoordinator;
        var workspace = coordinator?.ActiveWorkspace;
        if (_pageState != PageLifecycleState.Ready
            || _providerResetInProgress is not null
            || coordinator is null
            || workspace is null
            || !string.Equals(_activeWorkspace?.Id, workspace.WorkspaceId, StringComparison.Ordinal))
        {
            return;
        }

        var confirmation = await ShowPromptAsync(
            $"Reset {workspace.Provider.DisplayName} website data?",
            $"This signs out all {_workspaces.Count(candidate => candidate.Provider?.Id == workspace.Provider.Id)} AI Drawer workspace(s) using this provider profile on this device and clears all of its local browsing data, including cookies, cache, site storage, remembered permissions, and browsing or download history. Your provider account and provider-hosted conversations are not deleted.",
            "Reset website data",
            "Cancel");

        if (!confirmation.IsPrimary)
        {
            return;
        }

        if (_pageState != PageLifecycleState.Ready
            || _providerResetInProgress is not null
            || !ReferenceEquals(coordinator.ActiveWorkspace, workspace)
            || !string.Equals(_activeWorkspace?.Id, workspace.WorkspaceId, StringComparison.Ordinal))
        {
            return;
        }

        var providerId = workspace.Provider.Id;
        var affectedWorkspaces = _workspaces.Where(candidate =>
                string.Equals(candidate.ProviderId, providerId, StringComparison.Ordinal))
            .ToArray();
        var previousLocators = affectedWorkspaces.ToDictionary(
            affectedWorkspace => affectedWorkspace.Id,
            affectedWorkspace => affectedWorkspace.RestoreLocator,
            StringComparer.Ordinal);
        _providerResetInProgress = providerId;
        WorkspaceActionsButton.IsEnabled = false;
        RecoveryPanel.Visibility = Visibility.Collapsed;
        try
        {
            foreach (var affectedWorkspace in affectedWorkspaces)
            {
                affectedWorkspace.SetRestoreLocator(null);
                affectedWorkspace.SuppressHomeFallbackExplanation();
            }

            if (!await PersistSessionAsync())
            {
                foreach (var affectedWorkspace in affectedWorkspaces)
                {
                    affectedWorkspace.SetRestoreLocator(previousLocators[affectedWorkspace.Id]);
                }

                return;
            }

            if (_pageState != PageLifecycleState.Ready
                || !ReferenceEquals(_workspaceCoordinator, coordinator)
                || !ReferenceEquals(coordinator.ActiveWorkspace, workspace)
                || !string.Equals(_activeWorkspace?.Id, workspace.WorkspaceId, StringComparison.Ordinal))
            {
                return;
            }

            if (!await coordinator.ResetActiveWorkspaceAsync(workspace.WorkspaceId))
            {
                return;
            }
        }
        finally
        {
            _providerResetInProgress = null;
            if (_pageState == PageLifecycleState.Ready
                && ReferenceEquals(coordinator.ActiveWorkspace, workspace))
            {
                WorkspaceActionsButton.IsEnabled = workspace.IsLive;
            }
        }

        if (_pageState == PageLifecycleState.Ready && _activeWorkspace is { } activeWorkspace)
        {
            await SelectWorkspaceAsync(activeWorkspace.Id);
        }
    }

    private async Task<WorkspaceTab?> CreateWorkspaceAsync()
    {
        if (_pageState != PageLifecycleState.Ready || _providerResetInProgress is not null)
        {
            return null;
        }

        if (_workspaces.Count >= WorkspaceSession.MaximumWorkspaceCount)
        {
            ShowStatus(
                "Workspace limit reached",
                $"AI Drawer keeps up to {WorkspaceSession.MaximumWorkspaceCount} recoverable workspaces in this version. Close one before creating another.",
                InfoBarSeverity.Warning);
            return null;
        }

        var workspace = new WorkspaceTab(GetNextHomeWorkspaceNumber());
        _workspaces.Add(workspace);
        AddWorkspaceTab(workspace);
        UpdateCloseButtonVisibility();
        await SelectWorkspaceAsync(workspace.Id);
        return workspace;
    }

    private async Task OpenProviderInActiveWorkspaceAsync(string providerId)
    {
        if (_pageState != PageLifecycleState.Ready
            || _providerResetInProgress is not null
            || _workspaceCoordinator is null
            || _activeWorkspace is null
            || !_activeWorkspace.IsHome)
        {
            return;
        }

        var workspace = _activeWorkspace;
        await OpenProviderInWorkspaceAsync(workspace, providerId);
    }

    private async Task OpenProviderInWorkspaceAsync(WorkspaceTab workspace, string providerId)
    {
        if (_pageState != PageLifecycleState.Ready
            || _providerResetInProgress is not null
            || _workspaceCoordinator is not { } coordinator
            || !_workspaces.Contains(workspace)
            || _closingWorkspaceIds.Contains(workspace.Id)
            || !workspace.IsHome)
        {
            return;
        }

        var provider = coordinator.Providers.FirstOrDefault(candidate => candidate.Id == providerId);
        if (provider is null)
        {
            return;
        }

        var workspaceNumber = GetNextWorkspaceNumber(provider);
        workspace.SelectProvider(provider, workspaceNumber);
        UpdateWorkspaceTab(workspace);
        if (ReferenceEquals(_activeWorkspace, workspace))
        {
            await SelectWorkspaceAsync(workspace.Id);
        }
        else
        {
            await PersistSessionAsync();
        }
    }

    private async Task SelectWorkspaceAsync(string workspaceId)
    {
        if (_pageState != PageLifecycleState.Ready
            || _providerResetInProgress is not null
            || _workspaceCoordinator is not { } coordinator
            || _closingWorkspaceIds.Contains(workspaceId)
            || _workspaces.FirstOrDefault(workspace => workspace.Id == workspaceId) is not { } workspace)
        {
            return;
        }

        var selectionVersion = ++_selectionVersion;
        _activeWorkspace = workspace;
        UpdateWorkspaceTabSelection();
        HideWorkspaceActivity();
        RecoveryPanel.Visibility = Visibility.Collapsed;
        WorkspaceActionsButton.IsEnabled = false;

        if (workspace.IsHome)
        {
            await coordinator.DeactivateActiveWorkspaceAsync();
            if (!IsCurrentSelection(selectionVersion, workspace))
            {
                return;
            }

            HomePanel.Visibility = Visibility.Visible;
            WebViewHost.Visibility = Visibility.Collapsed;
            AnimateIn(HomePanel);
            CompatibilityStatusText.Visibility = Visibility.Collapsed;
            StatusBanner.Visibility = Visibility.Collapsed;
            UpdateSupportReminderVisibility();
            await PersistSessionAsync();
            return;
        }

        if (workspace.IsProviderUnavailable)
        {
            await coordinator.DeactivateActiveWorkspaceAsync();
            if (!IsCurrentSelection(selectionVersion, workspace))
            {
                return;
            }

            HomePanel.Visibility = Visibility.Collapsed;
            WebViewHost.Visibility = Visibility.Collapsed;
            CompatibilityStatusText.Visibility = Visibility.Collapsed;
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
        _updatingSettingsUi = true;
        KeepActiveToggle.IsOn = workspace.KeepActive;
        _updatingSettingsUi = false;
        HomeSupportReminder.Visibility = Visibility.Collapsed;
        var activated = await coordinator.ActivateAsync(
            workspace.Id,
            provider,
            _settings.RestoreExactWorkspace ? workspace.RestoreLocator : null,
            workspace.KeepActive,
            _settings.RestoreExactWorkspace && workspace.ShouldExplainHomeFallback);
        if (!IsCurrentSelection(selectionVersion, workspace))
        {
            return;
        }

        if (activated
            && _settings.RestoreExactWorkspace
            && coordinator.TryPromoteCommittedRestoreLocator(workspace.Id, out var restoreLocator))
        {
            workspace.SetRestoreLocator(restoreLocator);
        }

        WorkspaceActionsButton.IsEnabled = activated;
        await PersistSessionAsync();
    }

    private bool IsCurrentSelection(long selectionVersion, WorkspaceTab workspace) =>
        _pageState == PageLifecycleState.Ready
        && selectionVersion == _selectionVersion
        && ReferenceEquals(_activeWorkspace, workspace);

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
        UpdateWorkspaceTab(workspace);
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
        if (_pageState != PageLifecycleState.Ready
            || !string.Equals(_activeWorkspace?.Id, request.WorkspaceId, StringComparison.Ordinal))
        {
            return new PermissionDecision(false, false);
        }

        var result = await ShowPromptAsync(
            $"Allow {request.PermissionKind} for {request.ProviderName}?",
            $"Requesting origin: {request.Origin}. The decision applies to the shared {request.ProviderName} provider profile on this device. AI Drawer will not grant it automatically.",
            "Allow",
            "Deny",
            showRememberChoice: true);

        if (_pageState != PageLifecycleState.Ready
            || !string.Equals(_activeWorkspace?.Id, request.WorkspaceId, StringComparison.Ordinal))
        {
            return new PermissionDecision(false, false);
        }

        return new PermissionDecision(result.IsPrimary, result.RememberDecision);
    }

    private async Task<bool> RestoreSessionWithRecoveryAsync()
    {
        while (_pageState == PageLifecycleState.Loading)
        {
            var result = await RestoreSessionAsync();
            if (!result.Status.RequiresExplicitRecovery())
            {
                return true;
            }

            var decision = await ShowSessionRecoveryAsync(result.Status);
            if (_pageState != PageLifecycleState.Loading || decision == SessionRecoveryDecision.Exit)
            {
                App.ExitApplication();
                return false;
            }

            if (decision == SessionRecoveryDecision.Retry)
            {
                continue;
            }

            var backupResult = await _sessionStore.BackupBlockedSessionAsync();
            if (backupResult != SessionBackupResult.Created)
            {
                ShowStatus(
                    "Session backup did not finish",
                    "The existing session was not changed. Retry the backup after another app releases the file, or exit without saving.",
                    InfoBarSeverity.Warning);
                continue;
            }

            if (result.Status != SessionLoadStatus.LocatorRecoveryRequired)
            {
                ClearRestoredWorkspaceTabs();
                EnsureHomeWorkspace();
            }

            ShowStatus(
                "Previous session backed up",
                "AI Drawer preserved the previous session file locally and will continue without overwriting it.",
                InfoBarSeverity.Informational);
            return true;
        }

        return false;
    }

    private async Task<SessionLoadResult> RestoreSessionAsync()
    {
        if (_workspaceCoordinator is null)
        {
            return new SessionLoadResult(SessionLoadStatus.TemporarilyUnavailable, RestoredSession.Empty);
        }

        ClearRestoredWorkspaceTabs();
        var result = await _sessionStore.LoadSessionAsync();
        if (_pageState != PageLifecycleState.Loading)
        {
            return result;
        }

        var session = result.Status is SessionLoadStatus.Loaded or SessionLoadStatus.LocatorRecoveryRequired
            ? result.Session
            : RestoredSession.Empty;
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

        if (_workspaces.Count == 0 && !result.Status.RequiresExplicitRecovery())
        {
            EnsureHomeWorkspace();
        }

        UpdateCloseButtonVisibility();
        _activeWorkspace = _workspaces.FirstOrDefault(workspace =>
            string.Equals(workspace.Id, session.ActiveWorkspaceId, StringComparison.Ordinal))
            ?? _workspaces.FirstOrDefault();
        return result;
    }

    private void ClearRestoredWorkspaceTabs()
    {
        _workspaces.Clear();
        _workspaceTabViews.Clear();
        WorkspaceTabs.Children.Clear();
        _activeWorkspace = null;
    }

    private void EnsureHomeWorkspace()
    {
        if (_workspaces.Count != 0)
        {
            return;
        }

        var workspace = new WorkspaceTab(1);
        _workspaces.Add(workspace);
        AddWorkspaceTab(workspace);
    }

    private Task<SessionRecoveryDecision> ShowSessionRecoveryAsync(SessionLoadStatus status)
    {
        SessionRecoveryTitle.Text = status switch
        {
            SessionLoadStatus.LocatorRecoveryRequired => "Conversation location needs recovery",
            SessionLoadStatus.TemporarilyUnavailable => "Previous workspace session is temporarily unavailable",
            SessionLoadStatus.TooLarge => "Previous workspace session is too large",
            SessionLoadStatus.NewerSchema => "Previous workspace session is from a newer version",
            SessionLoadStatus.UnsupportedSchema => "Previous workspace session uses an unsupported format",
            _ => "Previous workspace session needs recovery"
        };
        SessionRecoveryMessage.Text = status switch
        {
            SessionLoadStatus.LocatorRecoveryRequired => "At least one encrypted conversation locator could not be read. The workspace list is still available, but saving now could permanently discard the unreadable locator.",
            SessionLoadStatus.TemporarilyUnavailable => "AI Drawer could not safely read the existing local session. It will not replace the file with an empty session while the cause is unknown.",
            SessionLoadStatus.TooLarge => "The local session exceeded the supported safety limit. AI Drawer will not load or overwrite it automatically.",
            SessionLoadStatus.NewerSchema => "This build cannot safely interpret a session written by a newer AI Drawer version.",
            SessionLoadStatus.UnsupportedSchema => "This build cannot safely interpret the local session format.",
            _ => "The local session could not be safely interpreted. AI Drawer will not overwrite it automatically."
        };
        _sessionRecoveryCompletion = new TaskCompletionSource<SessionRecoveryDecision>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        SessionRecoveryOverlay.Visibility = Visibility.Visible;
        SessionRecoveryRetryButton.Focus(FocusState.Programmatic);
        return _sessionRecoveryCompletion.Task;
    }

    private void SessionRecoveryRetryButton_Click(object sender, RoutedEventArgs e) =>
        CompleteSessionRecovery(SessionRecoveryDecision.Retry);

    private void SessionRecoveryBackupButton_Click(object sender, RoutedEventArgs e) =>
        CompleteSessionRecovery(SessionRecoveryDecision.BackupAndContinue);

    private void SessionRecoveryExitButton_Click(object sender, RoutedEventArgs e) =>
        CompleteSessionRecovery(SessionRecoveryDecision.Exit);

    private void CompleteSessionRecovery(SessionRecoveryDecision decision)
    {
        var completion = _sessionRecoveryCompletion;
        _sessionRecoveryCompletion = null;
        SessionRecoveryOverlay.Visibility = Visibility.Collapsed;
        completion?.TrySetResult(decision);
    }

    private async Task<bool> PersistSessionAsync()
    {
        try
        {
            await _sessionStore.SaveSessionAsync(
                _workspaces.Where(workspace => !_closingWorkspaceIds.Contains(workspace.Id)).ToArray(),
                _activeWorkspace?.Id,
                _settings.RestoreExactWorkspace);
            return true;
        }
        catch (Exception exception)
        {
            ShowStatus(
                "Workspace changes could not be saved",
                $"The current session is still available, but restart restore may be incomplete ({exception.GetType().Name}).",
                InfoBarSeverity.Warning);
            return false;
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

    private async Task ShowWelcomeAsync(WelcomeDisclosureMode mode)
    {
        IReadOnlyList<WelcomeDisclosure> disclosures = mode switch
        {
            WelcomeDisclosureMode.UpdatedPrivacyBoundary =>
            [
                new WelcomeDisclosure(
                    "Privacy and navigation update",
                    "AI Drawer now keeps unreviewed sites out of embedded workspaces. External links require your confirmation before opening in your browser. Known subscription, billing, and payment routes are blocked in AI Drawer; use a browser you trust to visit a provider website yourself. AI Drawer remains independent and unofficial, and never reads prompts, responses, page content, credentials, cookies, tokens, or payment data.")
            ],
            _ =>
            [
                new WelcomeDisclosure(
                    "Welcome to AI Drawer",
                    "AI Drawer is an independent, unofficial desktop shell for official AI websites. Use Win + Shift + A to show or hide it, or use the tray icon. Provider compatibility labels describe current evidence; your accounts, conversations, and subscriptions remain with each provider."),
                new WelcomeDisclosure(
                    "Your local session and privacy",
                    "Each provider uses its own local browser profile. Multiple AI Drawer workspaces can share that provider sign-in. AI Drawer never reads or stores prompts, responses, page content, credentials, cookies, tokens, or payment data. Clearing cache is not the same as resetting provider website data; reset signs out every workspace that shares that provider profile."),
                new WelcomeDisclosure(
                    "External links and purchases",
                    "AI Drawer embeds only reviewed provider and authentication origins. External links stay outside the app and require your confirmation before opening in your browser. Known subscription, billing, and payment routes are blocked here; use Edge, Chrome, or another browser you trust to visit a provider website yourself.")
            ]
        };

        foreach (var disclosure in disclosures)
        {
            var decision = await ShowPromptAsync(
                disclosure.Title,
                disclosure.Message,
                "Continue",
                "Skip tour");
            if (!decision.IsPrimary)
            {
                return;
            }
        }
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
        await ShowWelcomeAsync(WelcomeDisclosureMode.Reference);
    }

    private async void RestoreExactWorkspaceToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_pageState != PageLifecycleState.Ready
            || _providerResetInProgress is not null
            || _updatingSettingsUi)
        {
            return;
        }

        _settings = _settings with { RestoreExactWorkspace = RestoreExactWorkspaceToggle.IsOn };
        if (!_settings.RestoreExactWorkspace)
        {
            _workspaceCoordinator?.ClearAllPersistedRestoreLocators();
            foreach (var workspace in _workspaces)
            {
                workspace.SetRestoreLocator(null);
                workspace.SuppressHomeFallbackExplanation();
            }
        }
        else if (_workspaceCoordinator is { } coordinator)
        {
            PromoteCommittedRestoreLocators(coordinator);
        }

        await PersistSettingsAsync();
        if (_pageState != PageLifecycleState.Ready)
        {
            return;
        }

        await PersistSessionAsync();
    }

    private void PromoteCommittedRestoreLocators(WorkspaceCoordinator coordinator)
    {
        foreach (var workspace in _workspaces.Where(workspace =>
                     !workspace.IsHome && !_closingWorkspaceIds.Contains(workspace.Id)))
        {
            if (!string.Equals(workspace.ProviderId, _providerResetInProgress, StringComparison.Ordinal)
                && coordinator.TryPromoteCommittedRestoreLocator(workspace.Id, out var restoreLocator))
            {
                workspace.SetRestoreLocator(restoreLocator);
            }
        }
    }

    private async void MemoryModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_pageState != PageLifecycleState.Ready
            || _providerResetInProgress is not null
            || _updatingSettingsUi
            || MemoryModeComboBox.SelectedItem is not ComboBoxItem { Tag: string tag })
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
        if (_pageState != PageLifecycleState.Ready
            || _providerResetInProgress is not null
            || _updatingSettingsUi
            || _activeWorkspace is null)
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
        if (_pageState != PageLifecycleState.Ready)
        {
            return;
        }

        try
        {
            if (!await Launcher.LaunchUriAsync(SupportUri))
            {
                ShowStatus(
                    "Support page could not be opened",
                    "Windows did not find an application that could open the HTTPS link.",
                    InfoBarSeverity.Warning);
            }
        }
        catch (Exception exception)
        {
            ShowStatus(
                "Support page could not be opened",
                $"Windows could not open the HTTPS link ({exception.GetType().Name}).",
                InfoBarSeverity.Warning);
        }
    }

    private async void SupportNotNowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pageState != PageLifecycleState.Ready)
        {
            return;
        }

        _settings = _settings with
        {
            SupportReminderSnoozedUntilUtc = DateTimeOffset.UtcNow.AddDays(90)
        };
        HomeSupportReminder.Visibility = Visibility.Collapsed;
        await PersistSettingsAsync();
    }

    private async void SupportNeverButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pageState != PageLifecycleState.Ready)
        {
            return;
        }

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
        string? secondaryButtonText,
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
        PromptSecondaryButton.Visibility = secondaryButtonText is null ? Visibility.Collapsed : Visibility.Visible;
        PromptRememberCheckBox.IsChecked = false;
        PromptRememberCheckBox.Visibility = showRememberChoice ? Visibility.Visible : Visibility.Collapsed;
        _promptCompletion = new TaskCompletionSource<PromptDecision>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        PromptOverlay.Visibility = Visibility.Visible;
        AnimateIn(PromptCard);
        _ = DispatcherQueue.TryEnqueue(() =>
            (secondaryButtonText is null ? PromptPrimaryButton : PromptSecondaryButton).Focus(FocusState.Programmatic));

        return _promptCompletion.Task;
    }

    private void PromptPrimaryButton_Click(object sender, RoutedEventArgs e) => CompletePrompt(true);

    private void PromptSecondaryButton_Click(object sender, RoutedEventArgs e) => CompletePrompt(false);

    private void PromptCancelShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_sessionRecoveryCompletion is not null)
        {
            args.Handled = true;
            CompleteSessionRecovery(SessionRecoveryDecision.Exit);
            return;
        }

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

    private enum PageLifecycleState
    {
        Unloaded,
        Loading,
        Ready,
        ShuttingDown
    }

    private sealed record WorkspaceTabView(Grid Container, Button Tab, Button Close);

    private enum SessionRecoveryDecision
    {
        Retry,
        BackupAndContinue,
        Exit
    }

    private sealed record PromptDecision(bool IsPrimary, bool RememberDecision);

    private enum WelcomeDisclosureMode
    {
        Full,
        UpdatedPrivacyBoundary,
        Reference
    }

    private sealed record WelcomeDisclosure(string Title, string Message);
}
