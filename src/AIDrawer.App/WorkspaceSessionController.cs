using AIDrawer.Core;

namespace AIDrawer;

internal sealed class WorkspaceSessionController
{
    private readonly List<WorkspaceTab> _workspaces = [];

    internal IReadOnlyList<WorkspaceTab> Workspaces => _workspaces;
    internal WorkspaceSessionStore Store { get; } = new();
    internal WorkspaceLayout Layout { get; private set; } = new();
    internal WorkspaceTab? FocusedWorkspace => Find(Layout.FocusedWorkspaceId);

    internal WorkspaceTab? Find(string? id) => _workspaces.FirstOrDefault(workspace => workspace.Id == id);

    internal void Add(WorkspaceTab workspace)
    {
        _workspaces.Add(workspace);
        Layout = Layout.Normalize(_workspaces.Select(item => item.Id));
    }

    internal void Remove(string id)
    {
        _workspaces.RemoveAll(workspace => workspace.Id == id);
        var otherPane = Layout.PrimaryWorkspaceId == id ? Layout.SecondaryWorkspaceId : Layout.PrimaryWorkspaceId;
        Layout = Layout.Normalize(_workspaces.Select(item => item.Id), otherPane);
    }

    internal void Clear()
    {
        _workspaces.Clear();
        Layout = new();
    }

    internal void Select(string id)
    {
        if (Find(id) is not null)
        {
            Layout = Layout.Select(id);
        }
    }

    internal void PairWith(string id)
    {
        if (Find(id) is { IsHome: false, IsProviderUnavailable: false }
            && FocusedWorkspace is { IsHome: false, IsProviderUnavailable: false })
        {
            Layout = Layout.PairWith(id);
        }
    }

    internal void EndSplit() => Layout = Layout.EndSplit();

    internal void SetPaneRatio(double ratio) => Layout = (Layout with { PrimaryPaneRatio = ratio })
        .Normalize(_workspaces.Select(item => item.Id));

    internal void RestoreLayout(WorkspaceLayout? layout, string? previousActiveId) =>
        Layout = (layout ?? new WorkspaceLayout(previousActiveId, null, previousActiveId))
            .Normalize(_workspaces.Select(item => item.Id), previousActiveId);

    internal void Move(string id, int destination)
    {
        var source = _workspaces.FindIndex(workspace => workspace.Id == id);
        if (source < 0 || destination < 0 || destination >= _workspaces.Count || source == destination)
        {
            return;
        }

        var workspace = _workspaces[source];
        _workspaces.RemoveAt(source);
        _workspaces.Insert(destination, workspace);
    }

    internal Task SaveAsync(bool restoreExactWorkspace, IReadOnlySet<string> closingIds)
    {
        var workspaces = _workspaces.Where(workspace => !closingIds.Contains(workspace.Id)).ToArray();
        var layout = Layout.Normalize(workspaces.Select(workspace => workspace.Id));
        return Store.SaveSessionAsync(workspaces, layout.FocusedWorkspaceId, restoreExactWorkspace, layout);
    }
}
