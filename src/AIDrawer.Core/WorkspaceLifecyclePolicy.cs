namespace AIDrawer.Core;

public sealed record LiveWorkspaceState(
    string Id,
    bool IsActive,
    bool KeepActive,
    DateTimeOffset ProtectedUntil,
    DateTimeOffset LastActivated);

public sealed class WorkspaceLifecyclePolicy(
    int steadyLiveLimit = 2,
    int hardLiveLimit = 3,
    TimeSpan? gracePeriod = null)
{
    public int SteadyLiveLimit { get; } = steadyLiveLimit > 0
        ? steadyLiveLimit
        : throw new ArgumentOutOfRangeException(nameof(steadyLiveLimit));

    public int HardLiveLimit { get; } = hardLiveLimit >= steadyLiveLimit
        ? hardLiveLimit
        : throw new ArgumentOutOfRangeException(nameof(hardLiveLimit));

    public TimeSpan GracePeriod { get; } = gracePeriod ?? TimeSpan.FromMinutes(5);

    public IReadOnlyList<string> SelectForDisposal(
        IReadOnlyCollection<LiveWorkspaceState> liveWorkspaces,
        DateTimeOffset now,
        bool enforceHardLimit)
    {
        var target = enforceHardLimit ? HardLiveLimit - 1 : SteadyLiveLimit;
        var removable = liveWorkspaces
            .Where(workspace => !workspace.IsActive)
            .Where(workspace => enforceHardLimit || workspace.ProtectedUntil <= now)
            .OrderBy(workspace => workspace.KeepActive)
            .ThenBy(workspace => workspace.LastActivated)
            .ToList();

        var removeCount = Math.Max(0, liveWorkspaces.Count - target);
        return removable.Take(removeCount).Select(workspace => workspace.Id).ToArray();
    }
}
