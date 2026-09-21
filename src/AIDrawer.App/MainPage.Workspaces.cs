using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace AIDrawer;

public sealed partial class MainPage
{
    private const double MinimumSplitWidth = 1000;
    private readonly Dictionary<string, WorkspaceStateChangedEventArgs> _workspaceStates = new(StringComparer.Ordinal);
    private bool _tabDialogOpen;
    private int _applyingLayout;
    private bool _showingTwoPanes;
    private string? _draggedWorkspaceId;

    private bool CanEditWorkspaces => _pageState == PageLifecycleState.Ready
        && _providerResetInProgress is null && _promptCompletion is null && !_tabDialogOpen;

    private bool CanPromptForWorkspace(string workspaceId) =>
        _pageState == PageLifecycleState.Ready && _session.Find(workspaceId) is not null
        && !_closingWorkspaceIds.Contains(workspaceId) && _providerResetInProgress is null;

    private async Task ApplyWorkspaceLayoutAsync(string? resumeWorkspaceId = null)
    {
        if (_pageState != PageLifecycleState.Ready || _providerResetInProgress is not null
            || _workspaceCoordinator is not { } coordinator)
        {
            return;
        }

        var version = ++_selectionVersion;
        var layout = _session.Layout;
        var visible = layout.VisibleWorkspaceIds(WorkspaceContent.ActualWidth >= MinimumSplitWidth)
            .Select(_session.Find).OfType<WorkspaceTab>()
            .Where(tab => !_closingWorkspaceIds.Contains(tab.Id)).ToArray();
        _showingTwoPanes = visible.Length == 2;
        UpdatePaneGeometry();
        UpdateWorkspaceTabSelection();
        UpdateProviderDataSettingsUi();
        UpdatePaneHeaders();
        HideWorkspaceActivity();
        RecoveryPanel.Visibility = Visibility.Collapsed;
        StatusBanner.Visibility = Visibility.Collapsed;
        WorkspaceActionsButton.IsEnabled = _activeWorkspace?.Provider is not null;
        HomePanel.Visibility = _activeWorkspace?.IsHome == true ? Visibility.Visible : Visibility.Collapsed;
        WebViewHost.Visibility = visible.Any(tab => tab.Provider is not null) ? Visibility.Visible : Visibility.Collapsed;
        CompatibilityStatusText.Visibility = _activeWorkspace?.Provider is null ? Visibility.Collapsed : Visibility.Visible;
        if (_activeWorkspace?.Provider is { } provider)
        {
            CompatibilityStatusText.Text = provider.CompatibilityStatus;
            ReloadActionText.Text = $"Reload {provider.DisplayName}";
            RestartActionText.Text = $"Restart {provider.DisplayName} workspace";
        }
        UpdateSupportReminderVisibility();

        _applyingLayout++;
        try
        {
            await coordinator.ApplyLayoutAsync(visible, layout.FocusedWorkspaceId, _settings.RestoreExactWorkspace, resumeWorkspaceId);
        }
        finally
        {
            _applyingLayout--;
        }
        if (_pageState != PageLifecycleState.Ready || version != _selectionVersion)
        {
            return;
        }
        if (_activeWorkspace is { IsProviderUnavailable: true } unavailable)
        {
            ShowStatus("Provider unavailable", $"The tab '{unavailable.DisplayName}' was preserved. Choose another available tab.", InfoBarSeverity.Warning);
        }
        else if (_activeWorkspace is { } focused && _workspaceStates.TryGetValue(focused.Id, out var state))
        {
            Workspace_StateChanged(this, state);
        }
        if (_settings.RestoreExactWorkspace)
        {
            PromoteCommittedRestoreLocators(coordinator);
        }
        await PersistSessionAsync();
    }

    private void UpdatePaneGeometry()
    {
        var left = _showingTwoPanes ? _session.Layout.PrimaryPaneRatio : 1;
        foreach (var grid in new[] { WorkspaceContent, WebViewHost, PaneHeaderBar })
        {
            grid.ColumnDefinitions[0].Width = new GridLength(left, GridUnitType.Star);
            grid.ColumnDefinitions[1].Width = new GridLength(_showingTwoPanes ? 8 : 0);
            grid.ColumnDefinitions[2].Width = _showingTwoPanes ? new GridLength(1 - left, GridUnitType.Star) : new GridLength(0);
        }
        var focusedColumn = _showingTwoPanes && _session.Layout.FocusedWorkspaceId == _session.Layout.SecondaryWorkspaceId ? 2 : 0;
        Grid.SetColumn(RecoveryPanel, focusedColumn);
        Grid.SetColumn(WorkspaceLoadingPanel, focusedColumn);
        PaneHeaderBar.Visibility = _session.Layout.IsSplit ? Visibility.Visible : Visibility.Collapsed;
        SecondaryPaneButton.Visibility = _showingTwoPanes ? Visibility.Visible : Visibility.Collapsed;
        PaneDivider.Visibility = _showingTwoPanes ? Visibility.Visible : Visibility.Collapsed;
        EndSplitButton.Visibility = _session.Layout.IsSplit ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdatePaneHeaders()
    {
        if (PrimaryPaneButton is null)
        {
            return;
        }
        var ids = _session.Layout.VisibleWorkspaceIds(_showingTwoPanes);
        var buttons = new[] { PrimaryPaneButton, SecondaryPaneButton };
        for (var index = 0; index < ids.Count; index++)
        {
            var tab = _session.Find(ids[index]);
            var status = _workspaceStates.TryGetValue(ids[index], out var state)
                && state.Severity is InfoBarSeverity.Error or InfoBarSeverity.Warning ? " — needs attention" : string.Empty;
            buttons[index].Content = (tab?.DisplayName ?? "Tab") + status;
            buttons[index].Tag = ids[index];
            AutomationProperties.SetName(buttons[index], $"Focus {tab?.DisplayName}{status}");
        }
    }

    private async void Workspace_FocusRequested(object? sender, string workspaceId)
    {
        if (_applyingLayout != 0 || !CanEditWorkspaces || _activeWorkspace?.Id == workspaceId
            || !_session.Layout.VisibleWorkspaceIds(_showingTwoPanes).Contains(workspaceId))
        {
            return;
        }
        _session.Select(workspaceId);
        _selectionVersion++;
        HideWorkspaceActivity();
        RecoveryPanel.Visibility = Visibility.Collapsed;
        StatusBanner.Visibility = Visibility.Collapsed;
        UpdatePaneGeometry();
        UpdateWorkspaceTabSelection();
        UpdateProviderDataSettingsUi();
        _workspaceCoordinator?.FocusWorkspace(workspaceId);
        if (_activeWorkspace?.Provider is { } provider)
        {
            CompatibilityStatusText.Text = provider.CompatibilityStatus;
            ReloadActionText.Text = $"Reload {provider.DisplayName}";
            RestartActionText.Text = $"Restart {provider.DisplayName} workspace";
            WorkspaceActionsButton.IsEnabled = true;
        }
        await PersistSessionAsync();
    }

    private async void WorkspaceContent_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_pageState == PageLifecycleState.Ready && _session.Layout.IsSplit
            && (e.PreviousSize.Width >= MinimumSplitWidth) != (e.NewSize.Width >= MinimumSplitWidth))
        {
            await ApplyWorkspaceLayoutAsync();
        }
    }

    private void PaneDivider_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (CanEditWorkspaces && _showingTwoPanes && WorkspaceContent.ActualWidth > 8)
        {
            _session.SetPaneRatio(_session.Layout.PrimaryPaneRatio + e.HorizontalChange / (WorkspaceContent.ActualWidth - 8));
            UpdatePaneGeometry();
        }
    }

    private async void PaneDivider_DragCompleted(object sender, DragCompletedEventArgs e) => await PersistSessionAsync();

    private async void PaneDivider_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (CanEditWorkspaces && e.Key is VirtualKey.Left or VirtualKey.Right)
        {
            e.Handled = true;
            _session.SetPaneRatio(_session.Layout.PrimaryPaneRatio + (e.Key == VirtualKey.Left ? -0.05 : 0.05));
            UpdatePaneGeometry();
            await PersistSessionAsync();
        }
    }

    private async void EndSplitButton_Click(object sender, RoutedEventArgs e)
    {
        if (CanEditWorkspaces)
        {
            _session.EndSplit();
            await ApplyWorkspaceLayoutAsync();
        }
    }

    private async Task NewProviderTabAsync(string providerId)
    {
        if (!CanEditWorkspaces || _workspaces.Count >= AIDrawer.Core.WorkspaceSession.MaximumWorkspaceCount)
        {
            return;
        }
        var provider = WorkspaceCoordinator.Providers.FirstOrDefault(item => item.Id == providerId);
        if (provider is null)
        {
            return;
        }
        var tab = new WorkspaceTab(GetNextHomeWorkspaceNumber());
        tab.SelectProvider(provider, GetNextWorkspaceNumber(provider));
        _session.Add(tab);
        AddWorkspaceTab(tab);
        UpdateCloseButtonVisibility();
        await SelectWorkspaceAsync(tab.Id);
    }

    private async void NewSameProviderButton_Click(object sender, RoutedEventArgs e)
    {
        WorkspaceActionsFlyout.Hide();
        if (_activeWorkspace?.ProviderId is { } id)
        {
            await NewProviderTabAsync(id);
        }
    }

    private async void CycleWorkspaceShortcut_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (!CanEditWorkspaces || _workspaces.Count == 0)
        {
            return;
        }
        var current = _workspaces.ToList().FindIndex(tab => tab.Id == _activeWorkspace?.Id);
        var direction = sender.Modifiers.HasFlag(VirtualKeyModifiers.Shift) ? -1 : 1;
        await SelectWorkspaceAsync(_workspaces[(current + direction + _workspaces.Count) % _workspaces.Count].Id);
    }

    private void ConfigureWorkspaceTab(Button button, WorkspaceTab tab)
    {
        var menu = new MenuFlyout();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            AddAction("Rename tab", () => RenameWorkspaceAsync(tab.Id));
            if (tab.ProviderId is { } providerId)
            {
                AddAction("New tab with this provider", () => NewProviderTabAsync(providerId));
            }
            if (tab.Id != _activeWorkspace?.Id && tab.Provider is not null && _activeWorkspace?.Provider is not null)
            {
                AddAction("Show beside current tab", async () =>
                {
                    _session.PairWith(tab.Id);
                    await ApplyWorkspaceLayoutAsync(tab.Id);
                });
            }
            var index = _workspaces.ToList().IndexOf(tab);
            if (index > 0)
            {
                AddAction("Move left", () => MoveWorkspaceAsync(tab.Id, index - 1));
            }
            if (index < _workspaces.Count - 1)
            {
                AddAction("Move right", () => MoveWorkspaceAsync(tab.Id, index + 1));
            }
            if (tab.Provider is not null)
            {
                AddAction("Release page…", () => ReleaseWorkspaceFromUiAsync(tab.Id));
            }

            void AddAction(string text, Func<Task> action)
            {
                var item = new MenuFlyoutItem { Text = text, IsEnabled = CanEditWorkspaces };
                item.Click += async (_, _) =>
                {
                    if (CanEditWorkspaces && _session.Find(tab.Id) is not null)
                    {
                        await action();
                    }
                };
                menu.Items.Add(item);
            }
        };
        button.ContextFlyout = menu;
        button.CanDrag = true;
        button.AllowDrop = true;
        button.DragStarting += (_, args) =>
        {
            if (!CanEditWorkspaces)
            {
                args.Cancel = true;
                return;
            }
            _draggedWorkspaceId = tab.Id;
            args.Data.RequestedOperation = DataPackageOperation.Move;
        };
        button.DropCompleted += (_, _) => _draggedWorkspaceId = null;
        button.DragOver += (_, args) => args.AcceptedOperation = CanEditWorkspaces && _draggedWorkspaceId is not null
            ? DataPackageOperation.Move : DataPackageOperation.None;
        button.Drop += async (_, args) =>
        {
            if (CanEditWorkspaces && _draggedWorkspaceId is { } id)
            {
                args.Handled = true;
                _draggedWorkspaceId = null;
                await MoveWorkspaceAsync(id, _workspaces.ToList().IndexOf(tab));
            }
        };
    }

    private async Task MoveWorkspaceAsync(string id, int index)
    {
        _session.Move(id, index);
        WorkspaceTabs.Children.Clear();
        foreach (var workspace in _workspaces)
        {
            WorkspaceTabs.Children.Add(_workspaceTabViews[workspace.Id].Container);
        }
        await PersistSessionAsync();
    }

    private async Task RenameWorkspaceAsync(string id)
    {
        if (!CanEditWorkspaces || _session.Find(id) is not { } tab)
        {
            return;
        }
        _tabDialogOpen = true;
        try
        {
            var input = new TextBox { Text = tab.DisplayName, MaxLength = 100, Header = "Tab name (saved on this device)" };
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Rename tab",
                Content = input,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };
            dialog.PrimaryButtonClick += (_, args) => args.Cancel = string.IsNullOrWhiteSpace(input.Text) || input.Text.Any(char.IsControl);
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && _session.Find(id) is not null)
            {
                tab.Rename(input.Text);
                UpdateWorkspaceTab(tab);
                UpdatePaneHeaders();
                await PersistSessionAsync();
            }
        }
        finally
        {
            _tabDialogOpen = false;
        }
    }

    private async void WorkspaceListButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditWorkspaces)
        {
            return;
        }
        _tabDialogOpen = true;
        string? selectedId = null;
        try
        {
            var search = new TextBox { PlaceholderText = "Find a tab by name", Header = "Open tabs" };
            var list = new ListView { MaxHeight = 320, SelectionMode = ListViewSelectionMode.Single };
            void Populate()
            {
                list.Items.Clear();
                foreach (var tab in _workspaces.Where(tab => tab.DisplayName.Contains(search.Text, StringComparison.OrdinalIgnoreCase)))
                {
                    list.Items.Add(new ListViewItem { Content = $"{tab.DisplayName} — {tab.Provider?.DisplayName ?? "Choose a provider"}", Tag = tab.Id });
                }
            }
            search.TextChanged += (_, _) => Populate();
            Populate();
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(search);
            content.Children.Add(list);
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Switch tab",
                Content = content,
                PrimaryButtonText = "Switch",
                CloseButtonText = "Cancel",
                IsPrimaryButtonEnabled = false
            };
            list.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = list.SelectedItem is not null;
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && list.SelectedItem is ListViewItem { Tag: string id })
            {
                selectedId = id;
            }
        }
        finally
        {
            _tabDialogOpen = false;
        }
        if (selectedId is not null)
        {
            await SelectWorkspaceAsync(selectedId);
        }
    }

    private async void ReleaseWorkspaceButton_Click(object sender, RoutedEventArgs e)
    {
        WorkspaceActionsFlyout.Hide();
        if (_activeWorkspace is { } tab)
        {
            await ReleaseWorkspaceFromUiAsync(tab.Id);
        }
    }

    private async Task ReleaseWorkspaceFromUiAsync(string id)
    {
        if (!CanEditWorkspaces || _session.Find(id) is not { } tab || _workspaceCoordinator is not { } coordinator)
        {
            return;
        }
        var result = await ShowPromptAsync("Release this page?",
            $"'{tab.DisplayName}' will need to reload. Unsent drafts, uploads, voice, and ongoing generation may be lost or interrupted. The tab and available conversation address stay on this device.",
            "Release page", "Keep open");
        if (!result.IsPrimary || !CanPromptForWorkspace(id))
        {
            return;
        }
        if (!await coordinator.ReleaseWorkspaceAsync(id))
        {
            ShowStatus("Page could not be released", "Wait for navigation, downloads, permissions, or popups to finish, then try again.", InfoBarSeverity.Warning);
            return;
        }
        await PersistSessionAsync();
    }
}
