namespace AIDrawer;

internal sealed class WorkspaceTab
{
    internal WorkspaceTab()
    {
        Id = Guid.NewGuid().ToString("N");
    }

    internal string Id { get; }

    internal ProviderDefinition? Provider { get; private set; }

    internal string DisplayName { get; private set; } = "New workspace";

    internal bool IsHome => Provider is null;

    internal void SelectProvider(ProviderDefinition provider, int providerWorkspaceNumber)
    {
        Provider = provider;
        DisplayName = providerWorkspaceNumber == 1
            ? provider.WorkspaceLabel
            : $"{provider.WorkspaceLabel} {providerWorkspaceNumber}";
    }
}
