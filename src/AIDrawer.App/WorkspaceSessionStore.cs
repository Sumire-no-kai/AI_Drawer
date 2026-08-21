using AIDrawer.Core;
using System.Text.Json;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;

namespace AIDrawer;

internal sealed class WorkspaceSessionStore
{
    private static readonly SemaphoreSlim StorageWriteLock = new(1, 1);
    private static readonly string AppDataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AI Drawer");

    private static readonly string SessionPath = Path.Combine(AppDataRoot, "workspaces-v1.json");
    private static readonly string SettingsPath = Path.Combine(AppDataRoot, "settings-v1.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal async Task<RestoredSession> LoadSessionAsync()
    {
        try
        {
            if (!File.Exists(SessionPath))
            {
                return RestoredSession.Empty;
            }

            if (new FileInfo(SessionPath).Length > 1024 * 1024)
            {
                return RestoredSession.Empty;
            }

            var json = await File.ReadAllTextAsync(SessionPath);
            var document = JsonSerializer.Deserialize<WorkspaceSession>(json, JsonOptions);
            if (document is null || document.SchemaVersion != WorkspaceSession.CurrentSchemaVersion)
            {
                return RestoredSession.Empty;
            }

            var restored = new List<RestoredWorkspace>();
            var restoredIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var workspace in document.Workspaces.Take(100))
            {
                if (string.IsNullOrWhiteSpace(workspace.Id)
                    || workspace.Id.Length > 64
                    || !restoredIds.Add(workspace.Id)
                    || string.IsNullOrWhiteSpace(workspace.DisplayName)
                    || workspace.DisplayName.Length > 100
                    || workspace.ProviderId?.Length > 50
                    || workspace.ProtectedRestoreLocator?.Length > 8192)
                {
                    continue;
                }

                var locator = await UnprotectAsync(workspace.ProtectedRestoreLocator);
                restored.Add(new RestoredWorkspace(
                    workspace.Id,
                    workspace.DisplayName,
                    workspace.ProviderId,
                    workspace.KeepActive,
                    locator));
            }

            return new RestoredSession(document.ActiveWorkspaceId, restored);
        }
        catch
        {
            return RestoredSession.Empty;
        }
    }

    internal async Task SaveSessionAsync(
        IReadOnlyList<WorkspaceTab> workspaces,
        string? activeWorkspaceId,
        bool restoreExactWorkspace)
    {
        var workspaceStates = workspaces.Select(workspace => new SessionWorkspaceState(
            workspace.Id,
            workspace.DisplayName,
            workspace.ProviderId,
            workspace.KeepActive,
            workspace.RestoreLocator?.AbsoluteUri)).ToArray();

        await StorageWriteLock.WaitAsync();
        try
        {
            Directory.CreateDirectory(AppDataRoot);
            var snapshots = new List<ConversationWorkspaceSnapshot>(workspaceStates.Length);
            foreach (var workspace in workspaceStates)
            {
                var protectedLocator = restoreExactWorkspace
                    ? await ProtectAsync(workspace.RestoreLocator)
                    : null;
                snapshots.Add(new ConversationWorkspaceSnapshot(
                    workspace.Id,
                    workspace.DisplayName,
                    workspace.ProviderId,
                    workspace.KeepActive,
                    protectedLocator));
            }

            var document = new WorkspaceSession(
                WorkspaceSession.CurrentSchemaVersion,
                activeWorkspaceId,
                snapshots);
            await WriteAtomicallyAsync(SessionPath, JsonSerializer.Serialize(document, JsonOptions));
        }
        finally
        {
            StorageWriteLock.Release();
        }
    }

    internal async Task<AppSettings> LoadSettingsAsync()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            if (new FileInfo(SettingsPath).Length > 64 * 1024)
            {
                return new AppSettings();
            }

            var json = await File.ReadAllTextAsync(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (settings is not { SchemaVersion: AppSettings.CurrentSchemaVersion })
            {
                return new AppSettings();
            }

            return settings with
            {
                MemoryMode = MemoryMode.Balanced,
                SuccessfulOpenCount = Math.Max(0, settings.SuccessfulOpenCount)
            };
        }
        catch
        {
            return new AppSettings();
        }
    }

    internal async Task SaveSettingsAsync(AppSettings settings)
    {
        await StorageWriteLock.WaitAsync();
        try
        {
            Directory.CreateDirectory(AppDataRoot);
            await WriteAtomicallyAsync(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        finally
        {
            StorageWriteLock.Release();
        }
    }

    private static async Task<string?> ProtectAsync(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var provider = new DataProtectionProvider("LOCAL=user");
            var input = CryptographicBuffer.ConvertStringToBinary(value, BinaryStringEncoding.Utf8);
            var protectedBuffer = await provider.ProtectAsync(input);
            return CryptographicBuffer.EncodeToBase64String(protectedBuffer);
        }
        catch
        {
            // Preserve non-sensitive workspace identity without weakening locator protection.
            return null;
        }
    }

    private static async Task<string?> UnprotectAsync(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var provider = new DataProtectionProvider();
            var protectedBuffer = CryptographicBuffer.DecodeFromBase64String(value);
            var clearBuffer = await provider.UnprotectAsync(protectedBuffer);
            return CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, clearBuffer);
        }
        catch
        {
            return null;
        }
    }

    private static async Task WriteAtomicallyAsync(string path, string content)
    {
        var temporaryPath = $"{path}.tmp";
        await File.WriteAllTextAsync(temporaryPath, content);
        File.Move(temporaryPath, path, overwrite: true);
    }
}

internal sealed record SessionWorkspaceState(
    string Id,
    string DisplayName,
    string? ProviderId,
    bool KeepActive,
    string? RestoreLocator);

internal sealed record RestoredSession(
    string? ActiveWorkspaceId,
    IReadOnlyList<RestoredWorkspace> Workspaces)
{
    internal static RestoredSession Empty { get; } = new(null, []);
}

internal sealed record RestoredWorkspace(
    string Id,
    string DisplayName,
    string? ProviderId,
    bool KeepActive,
    string? RestoreLocator);
