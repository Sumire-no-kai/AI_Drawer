using AIDrawer.Core;
using System.Globalization;
using System.Text.Json;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;

namespace AIDrawer;

internal sealed class WorkspaceSessionStore
{
    private const int MaximumSessionBytes = 1024 * 1024;
    private static readonly object StorageWriteQueueGate = new();
    private static Task StorageWriteTail = Task.CompletedTask;
    private static readonly string AppDataRoot = ApplicationDataPaths.AppDataRoot;

    private static readonly string SessionPath = Path.Combine(AppDataRoot, "workspaces-v1.json");
    private static readonly string SettingsPath = Path.Combine(AppDataRoot, "settings-v1.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private bool _sessionWritesBlocked;

    internal async Task<SessionLoadResult> LoadSessionAsync()
    {
        try
        {
            var json = await ReadSessionFileAsync();
            if (json is null)
            {
                return SetSessionLoadResult(SessionLoadStatus.Missing, RestoredSession.Empty);
            }

            var document = JsonSerializer.Deserialize<WorkspaceSession>(json, JsonOptions);
            if (document is null)
            {
                return SetSessionLoadResult(SessionLoadStatus.Corrupt, RestoredSession.Empty);
            }

            if (document.SchemaVersion != WorkspaceSession.CurrentSchemaVersion)
            {
                return SetSessionLoadResult(
                    document.SchemaVersion > WorkspaceSession.CurrentSchemaVersion
                        ? SessionLoadStatus.NewerSchema
                        : SessionLoadStatus.UnsupportedSchema,
                    RestoredSession.Empty);
            }

            var restored = new List<RestoredWorkspace>();
            var restoredIds = new HashSet<string>(StringComparer.Ordinal);
            var hadInvalidWorkspace = false;
            var hadLocatorFailure = false;
            foreach (var workspace in document.Workspaces ?? [])
            {
                if (string.IsNullOrWhiteSpace(workspace.Id)
                    || workspace.Id.Length > 64
                    || !restoredIds.Add(workspace.Id)
                    || string.IsNullOrWhiteSpace(workspace.DisplayName)
                    || workspace.DisplayName.Length > 100
                    || workspace.ProviderId?.Length > 50
                    || workspace.ProtectedRestoreLocator?.Length > 8192)
                {
                    hadInvalidWorkspace = true;
                    break;
                }

                if (restored.Count >= WorkspaceSession.MaximumWorkspaceCount)
                {
                    hadInvalidWorkspace = true;
                    break;
                }

                var locatorResult = await UnprotectAsync(workspace.ProtectedRestoreLocator);
                if (locatorResult.Failed)
                {
                    hadLocatorFailure = true;
                }

                restored.Add(new RestoredWorkspace(
                    workspace.Id,
                    workspace.DisplayName,
                    workspace.ProviderId,
                    workspace.KeepActive,
                    locatorResult.Value));
            }

            var session = new RestoredSession(document.ActiveWorkspaceId, restored);
            if (hadInvalidWorkspace)
            {
                return SetSessionLoadResult(SessionLoadStatus.Corrupt, session);
            }

            return SetSessionLoadResult(
                hadLocatorFailure ? SessionLoadStatus.LocatorRecoveryRequired : SessionLoadStatus.Loaded,
                session);
        }
        catch (SessionFileTooLargeException)
        {
            return SetSessionLoadResult(SessionLoadStatus.TooLarge, RestoredSession.Empty);
        }
        catch (JsonException)
        {
            return SetSessionLoadResult(SessionLoadStatus.Corrupt, RestoredSession.Empty);
        }
        catch (NotSupportedException)
        {
            return SetSessionLoadResult(SessionLoadStatus.Corrupt, RestoredSession.Empty);
        }
        catch (FileNotFoundException)
        {
            return SetSessionLoadResult(SessionLoadStatus.Missing, RestoredSession.Empty);
        }
        catch (DirectoryNotFoundException)
        {
            return SetSessionLoadResult(SessionLoadStatus.Missing, RestoredSession.Empty);
        }
        catch (IOException)
        {
            return SetSessionLoadResult(SessionLoadStatus.TemporarilyUnavailable, RestoredSession.Empty);
        }
        catch (UnauthorizedAccessException)
        {
            return SetSessionLoadResult(SessionLoadStatus.TemporarilyUnavailable, RestoredSession.Empty);
        }
        catch
        {
            return SetSessionLoadResult(SessionLoadStatus.TemporarilyUnavailable, RestoredSession.Empty);
        }
    }

    internal Task SaveSessionAsync(
        IReadOnlyList<WorkspaceTab> workspaces,
        string? activeWorkspaceId,
        bool restoreExactWorkspace)
    {
        if (_sessionWritesBlocked)
        {
            return Task.FromException(new SessionWriteBlockedException());
        }

        var workspaceStates = workspaces
            .Take(WorkspaceSession.MaximumWorkspaceCount)
            .Select(workspace => new SessionWorkspaceState(
                workspace.Id,
                workspace.DisplayName,
                workspace.ProviderId,
                workspace.KeepActive,
                workspace.Provider?.CreateRestoreLocator(workspace.RestoreLocator?.AbsoluteUri)?.AbsoluteUri))
            .ToArray();
        var persistedActiveWorkspaceId = workspaceStates.Any(workspace =>
            string.Equals(workspace.Id, activeWorkspaceId, StringComparison.Ordinal))
            ? activeWorkspaceId
            : null;

        return EnqueueStorageWriteAsync(async () =>
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
                persistedActiveWorkspaceId,
                snapshots);
            await WriteAtomicallyAsync(SessionPath, JsonSerializer.Serialize(document, JsonOptions));
        });
    }

    internal static async Task<AppSettings> LoadSettingsAsync()
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

    internal static Task SaveSettingsAsync(AppSettings settings) =>
        EnqueueStorageWriteAsync(async () =>
        {
            Directory.CreateDirectory(AppDataRoot);
            await WriteAtomicallyAsync(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        });

    internal static Task FlushWritesAsync()
    {
        lock (StorageWriteQueueGate)
        {
            return StorageWriteTail;
        }
    }

    internal async Task<SessionBackupResult> BackupBlockedSessionAsync()
    {
        if (!_sessionWritesBlocked)
        {
            return SessionBackupResult.NotRequired;
        }

        SessionBackupResult result = SessionBackupResult.Failed;
        await EnqueueStorageWriteAsync(() =>
        {
            try
            {
                var backupPath = CreateBackupPath();
                if (!IsSafeSessionBackupPath(backupPath) || !File.Exists(SessionPath))
                {
                    return Task.CompletedTask;
                }

                File.Move(SessionPath, backupPath, overwrite: false);
                _sessionWritesBlocked = false;
                result = SessionBackupResult.Created;
            }
            catch (IOException)
            {
                // Keep the write gate closed; the user can retry or exit without overwriting the source.
            }
            catch (UnauthorizedAccessException)
            {
                // Keep the write gate closed; the user can retry or exit without overwriting the source.
            }

            return Task.CompletedTask;
        });
        return result;
    }

    private static Task EnqueueStorageWriteAsync(Func<Task> writeAsync)
    {
        lock (StorageWriteQueueGate)
        {
            StorageWriteTail = WriteAfterAsync(StorageWriteTail, writeAsync);
            return StorageWriteTail;
        }
    }

    private static async Task WriteAfterAsync(Task predecessor, Func<Task> writeAsync)
    {
        try
        {
            await predecessor;
        }
        catch
        {
            // A failed write is reported to its own caller and must not stop later snapshots.
        }

        await writeAsync();
    }

    private static async Task<string?> ProtectAsync(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var provider = new DataProtectionProvider("LOCAL=user");
        var input = CryptographicBuffer.ConvertStringToBinary(value, BinaryStringEncoding.Utf8);
        var protectedBuffer = await provider.ProtectAsync(input);
        return CryptographicBuffer.EncodeToBase64String(protectedBuffer);
    }

    private SessionLoadResult SetSessionLoadResult(SessionLoadStatus status, RestoredSession session)
    {
        _sessionWritesBlocked = status.RequiresExplicitRecovery();
        return new SessionLoadResult(status, session);
    }

    private static async Task<string?> ReadSessionFileAsync()
    {
        await using var stream = new FileStream(
            SessionPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        if (stream.Length > MaximumSessionBytes)
        {
            throw new SessionFileTooLargeException();
        }

        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static async Task<ProtectedValueLoadResult> UnprotectAsync(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new ProtectedValueLoadResult(null, false);
        }

        try
        {
            var provider = new DataProtectionProvider();
            var protectedBuffer = CryptographicBuffer.DecodeFromBase64String(value);
            var clearBuffer = await provider.UnprotectAsync(protectedBuffer);
            return new ProtectedValueLoadResult(
                CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, clearBuffer),
                false);
        }
        catch
        {
            return new ProtectedValueLoadResult(null, true);
        }
    }

    private static string CreateBackupPath()
    {
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);
        return Path.Combine(AppDataRoot, $"workspaces-v1.{stamp}.recovery-backup.json");
    }

    private static bool IsSafeSessionBackupPath(string backupPath)
    {
        var root = Path.GetFullPath(AppDataRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var session = Path.GetFullPath(SessionPath);
        var candidate = Path.GetFullPath(backupPath);
        return string.Equals(Path.GetDirectoryName(candidate), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            && candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            && candidate.EndsWith(".recovery-backup.json", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetDirectoryName(session), Path.GetDirectoryName(candidate), StringComparison.OrdinalIgnoreCase);
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

internal enum SessionLoadStatus
{
    Missing,
    Loaded,
    LocatorRecoveryRequired,
    Corrupt,
    TooLarge,
    UnsupportedSchema,
    NewerSchema,
    TemporarilyUnavailable
}

internal static class SessionLoadStatusExtensions
{
    internal static bool RequiresExplicitRecovery(this SessionLoadStatus status) => status is not SessionLoadStatus.Missing and not SessionLoadStatus.Loaded;
}

internal sealed record SessionLoadResult(SessionLoadStatus Status, RestoredSession Session);

internal enum SessionBackupResult
{
    Created,
    NotRequired,
    Failed
}

internal sealed class SessionWriteBlockedException : InvalidOperationException
{
    internal SessionWriteBlockedException()
        : base("Session writes are blocked until the existing session file is backed up.")
    {
    }
}

internal sealed class SessionFileTooLargeException : IOException
{
}

internal sealed record ProtectedValueLoadResult(string? Value, bool Failed);
