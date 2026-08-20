namespace AIDrawer;

internal sealed class WorkspaceTab
{
    internal WorkspaceTab(int workspaceNumber)
    {
        Id = Guid.NewGuid().ToString("N");
        DisplayName = workspaceNumber == 1 ? "New workspace" : $"New workspace {workspaceNumber}";
    }

    internal string Id { get; }

    internal ProviderDefinition? Provider { get; private set; }

    internal string DisplayName { get; private set; }

    internal bool IsHome => Provider is null;

    internal void SelectProvider(ProviderDefinition provider, int providerWorkspaceNumber)
    {
        Provider = provider;
        DisplayName = providerWorkspaceNumber == 1
            ? provider.WorkspaceLabel
            : $"{provider.WorkspaceLabel} {providerWorkspaceNumber}";
    }
}
