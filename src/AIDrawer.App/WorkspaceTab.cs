namespace AIDrawer;

internal sealed class WorkspaceTab
{
    internal WorkspaceTab(int workspaceNumber)
        : this(
            Guid.NewGuid().ToString("N"),
            workspaceNumber == 1 ? "New workspace" : $"New workspace {workspaceNumber}",
            provider: null,
            providerId: null,
            keepActive: false,
            restoreLocator: null,
            wasRestoredFromSession: false)
    {
    }

    internal WorkspaceTab(
        string id,
        string displayName,
        ProviderDefinition? provider,
        string? providerId,
        bool keepActive,
        Uri? restoreLocator,
        bool wasRestoredFromSession = true)
    {
        Id = id;
        DisplayName = displayName;
        Provider = provider;
        ProviderId = provider?.Id ?? providerId;
        KeepActive = keepActive;
        RestoreLocator = restoreLocator;
        ShouldExplainHomeFallback = wasRestoredFromSession
            && ProviderId is not null
            && restoreLocator is null;
        LifecyclePhase = wasRestoredFromSession && ProviderId is not null
            ? WorkspaceLifecyclePhase.Disposed
            : null;
    }

    internal string Id { get; }

    internal ProviderDefinition? Provider { get; private set; }

    internal string? ProviderId { get; private set; }

    internal string DisplayName { get; private set; }

    internal bool KeepActive { get; private set; }

    internal Uri? RestoreLocator { get; private set; }

    internal WorkspaceLifecyclePhase? LifecyclePhase { get; private set; }

    internal bool ShouldExplainHomeFallback { get; private set; }

    internal bool IsHome => ProviderId is null;

    internal bool IsProviderUnavailable => ProviderId is not null && Provider is null;

    internal void SelectProvider(ProviderDefinition provider, int providerWorkspaceNumber)
    {
        Provider = provider;
        ProviderId = provider.Id;
        DisplayName = providerWorkspaceNumber == 1
            ? provider.WorkspaceLabel
            : $"{provider.WorkspaceLabel} {providerWorkspaceNumber}";
    }

    internal void SetKeepActive(bool keepActive) => KeepActive = keepActive;

    internal void SetRestoreLocator(Uri? restoreLocator) => RestoreLocator = restoreLocator;

    internal void SuppressHomeFallbackExplanation() => ShouldExplainHomeFallback = false;

    internal void SetLifecyclePhase(WorkspaceLifecyclePhase phase) => LifecyclePhase = phase;
}
