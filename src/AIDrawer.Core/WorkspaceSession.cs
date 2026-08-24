namespace AIDrawer.Core;

public sealed record WorkspaceSession(
    int SchemaVersion,
    string? ActiveWorkspaceId,
    IReadOnlyList<ConversationWorkspaceSnapshot> Workspaces)
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumWorkspaceCount = 100;
}

public sealed record ConversationWorkspaceSnapshot(
    string Id,
    string DisplayName,
    string? ProviderId,
    bool KeepActive,
    string? ProtectedRestoreLocator);

public sealed record AppSettings(
    int SchemaVersion = AppSettings.CurrentSchemaVersion,
    int OnboardingVersion = 0,
    bool RestoreExactWorkspace = true,
    MemoryMode MemoryMode = MemoryMode.Balanced,
    DateTimeOffset? FirstUsedUtc = null,
    int SuccessfulOpenCount = 0,
    bool SupportReminderDismissed = false,
    DateTimeOffset? SupportReminderSnoozedUntilUtc = null,
    int SupportReminderSnoozedUntilMajorRelease = 0,
    string? DefaultProviderId = null,
    GlobalShortcutSettings? GlobalShortcut = null,
    bool LaunchOnStartup = false,
    bool CloseToTray = true,
    bool AlwaysOnTop = false,
    WindowPlacementSnapshot? WindowPlacement = null)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record WindowPlacementSnapshot(
    int X,
    int Y,
    int Width,
    int Height);

public enum MemoryMode
{
    LowMemory,
    Balanced,
    FastSwitching
}
