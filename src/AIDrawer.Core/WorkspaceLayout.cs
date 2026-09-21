namespace AIDrawer.Core;

public sealed record WorkspaceLayout(
    string? PrimaryWorkspaceId = null,
    string? SecondaryWorkspaceId = null,
    string? FocusedWorkspaceId = null,
    double PrimaryPaneRatio = 0.5)
{
    public bool IsSplit => SecondaryWorkspaceId is not null;

    public WorkspaceLayout Normalize(IEnumerable<string> workspaceIds, string? fallbackId = null)
    {
        var ids = workspaceIds.ToHashSet(StringComparer.Ordinal);
        var primary = PrimaryWorkspaceId is not null && ids.Contains(PrimaryWorkspaceId)
            ? PrimaryWorkspaceId
            : fallbackId is not null && ids.Contains(fallbackId) ? fallbackId : ids.FirstOrDefault();
        var secondary = SecondaryWorkspaceId is not null
            && ids.Contains(SecondaryWorkspaceId) && SecondaryWorkspaceId != primary
                ? SecondaryWorkspaceId : null;
        var focus = FocusedWorkspaceId == primary || secondary is not null && FocusedWorkspaceId == secondary
            ? FocusedWorkspaceId : primary;
        return new(primary, secondary, focus,
            double.IsFinite(PrimaryPaneRatio) ? Math.Clamp(PrimaryPaneRatio, 0.25, 0.75) : 0.5);
    }

    public WorkspaceLayout Select(string workspaceId)
    {
        if (workspaceId == PrimaryWorkspaceId || workspaceId == SecondaryWorkspaceId)
        {
            return this with { FocusedWorkspaceId = workspaceId };
        }

        return IsSplit && FocusedWorkspaceId == SecondaryWorkspaceId
            ? this with { SecondaryWorkspaceId = workspaceId, FocusedWorkspaceId = workspaceId }
            : this with { PrimaryWorkspaceId = workspaceId, FocusedWorkspaceId = workspaceId };
    }

    public WorkspaceLayout PairWith(string workspaceId) =>
        FocusedWorkspaceId is null || FocusedWorkspaceId == workspaceId
            ? this
            : new(FocusedWorkspaceId, workspaceId, workspaceId, PrimaryPaneRatio);

    public WorkspaceLayout EndSplit() => new(FocusedWorkspaceId, null, FocusedWorkspaceId, PrimaryPaneRatio);

    public IReadOnlyList<string> VisibleWorkspaceIds(bool wideEnough) =>
        IsSplit && wideEnough
            ? [PrimaryWorkspaceId!, SecondaryWorkspaceId!]
            : FocusedWorkspaceId is { } id ? [id] : [];
}
