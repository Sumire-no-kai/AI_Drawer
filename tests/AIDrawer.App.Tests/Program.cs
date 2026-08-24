using AIDrawer;
using AIDrawer.Core;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;

var failures = new List<string>();
var runUiChecks = !args.Contains("--no-ui", StringComparer.OrdinalIgnoreCase);
var completedCheckCount = 0;
var testRoot = Path.Combine(Path.GetTempPath(), $"AI-Drawer-SessionTests-{Guid.NewGuid():N}");
Environment.SetEnvironmentVariable("AI_DRAWER_TEST_DATA_ROOT", testRoot);
var appDataRoot = Path.Combine(testRoot, "AI Drawer");
var sessionPath = Path.Combine(appDataRoot, "workspaces-v1.json");
var settingsPath = Path.Combine(appDataRoot, "settings-v1.json");

try
{
    await CheckAsync("corrupt session is preserved until an explicit backup", async () =>
    {
        const string corruptSession = "{ not valid json";
        await WriteSessionAsync(corruptSession);

        var store = new WorkspaceSessionStore();
        Equal(SessionLoadStatus.Corrupt, (await store.LoadSessionAsync()).Status);
        await ThrowsAsync<SessionWriteBlockedException>(() => store.SaveSessionAsync([], null, restoreExactWorkspace: false));
        Equal(corruptSession, await File.ReadAllTextAsync(sessionPath));

        Equal(SessionBackupResult.Created, await store.BackupBlockedSessionAsync());
        False(File.Exists(sessionPath));
        var backupPath = Directory.GetFiles(appDataRoot, "*.recovery-backup.json").Single();
        Equal(corruptSession, await File.ReadAllTextAsync(backupPath));

        await store.SaveSessionAsync([new WorkspaceTab(1)], null, restoreExactWorkspace: false);
        True(File.Exists(sessionPath));
    });

    await CheckAsync("oversized session is not read or overwritten", async () =>
    {
        await WriteSessionAsync(new string('x', 1024 * 1024 + 1));

        var store = new WorkspaceSessionStore();
        Equal(SessionLoadStatus.TooLarge, (await store.LoadSessionAsync()).Status);
        await ThrowsAsync<SessionWriteBlockedException>(() => store.SaveSessionAsync([], null, restoreExactWorkspace: false));
        Equal(1024 * 1024 + 1L, new FileInfo(sessionPath).Length);
    });

    await CheckAsync("newer schema is not interpreted or overwritten", async () =>
    {
        await WriteSessionAsync("""
            { "SchemaVersion": 2, "ActiveWorkspaceId": null, "Workspaces": [] }
            """);

        var store = new WorkspaceSessionStore();
        Equal(SessionLoadStatus.NewerSchema, (await store.LoadSessionAsync()).Status);
        await ThrowsAsync<SessionWriteBlockedException>(() => store.SaveSessionAsync([], null, restoreExactWorkspace: false));
    });

    await CheckAsync("a bad DPAPI locator preserves workspace metadata and requires recovery", async () =>
    {
        await WriteSessionAsync("""
            {
              "SchemaVersion": 1,
              "ActiveWorkspaceId": "workspace-1",
              "Workspaces": [
                {
                  "Id": "workspace-1",
                  "DisplayName": "ChatGPT",
                  "ProviderId": "chatgpt",
                  "KeepActive": false,
                  "ProtectedRestoreLocator": "not-a-valid-dpapi-value"
                }
              ]
            }
            """);

        var store = new WorkspaceSessionStore();
        var result = await store.LoadSessionAsync();
        Equal(SessionLoadStatus.LocatorRecoveryRequired, result.Status);
        Equal(1, result.Session.Workspaces.Count);
        Equal("workspace-1", result.Session.Workspaces.Single().Id);
        Null(result.Session.Workspaces.Single().RestoreLocator);
        await ThrowsAsync<SessionWriteBlockedException>(() => store.SaveSessionAsync([], null, restoreExactWorkspace: false));
    });

    await CheckAsync("a reviewed locator survives a DPAPI save and load round trip", async () =>
    {
        await DeleteDirectoryWhenReleasedAsync(appDataRoot);
        var provider = ProviderCatalog.AvailableProviders.Single(candidate => candidate.Id == "chatgpt");
        var locator = new Uri("https://chatgpt.com/c/opaque-validation-id");
        var workspace = new WorkspaceTab(
            "workspace-1",
            "ChatGPT",
            provider,
            provider.Id,
            keepActive: false,
            locator,
            wasRestoredFromSession: false);
        var store = new WorkspaceSessionStore();

        await store.SaveSessionAsync([workspace], workspace.Id, restoreExactWorkspace: true);
        var persisted = await File.ReadAllTextAsync(sessionPath);
        False(persisted.Contains(locator.AbsoluteUri, StringComparison.Ordinal));

        var result = await store.LoadSessionAsync();
        Equal(SessionLoadStatus.Loaded, result.Status);
        Equal(locator, new Uri(result.Session.Workspaces.Single().RestoreLocator!));
    });

    await CheckAsync("an exclusively locked session is treated as temporary and never overwritten", async () =>
    {
        await WriteSessionAsync("""
            { "SchemaVersion": 1, "ActiveWorkspaceId": null, "Workspaces": [] }
            """);
        await using var lockStream = new FileStream(sessionPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var store = new WorkspaceSessionStore();
        Equal(SessionLoadStatus.TemporarilyUnavailable, (await store.LoadSessionAsync()).Status);
        await ThrowsAsync<SessionWriteBlockedException>(() => store.SaveSessionAsync([], null, restoreExactWorkspace: false));
    });

    await CheckAsync("session persistence keeps the configured 100-workspace ceiling", async () =>
    {
        await DeleteDirectoryWhenReleasedAsync(appDataRoot);
        var workspaces = Enumerable.Range(1, WorkspaceSession.MaximumWorkspaceCount + 1)
            .Select(number => new WorkspaceTab(number))
            .ToArray();
        var store = new WorkspaceSessionStore();
        await store.SaveSessionAsync(workspaces, workspaces.Last().Id, restoreExactWorkspace: false);

        var persisted = JsonSerializer.Deserialize<WorkspaceSession>(await File.ReadAllTextAsync(sessionPath));
        True(persisted is not null);
        Equal(WorkspaceSession.MaximumWorkspaceCount, persisted!.Workspaces.Count);
        Null(persisted.ActiveWorkspaceId);
        False(File.Exists($"{sessionPath}.tmp"));
    });

    await CheckAsync("MVP shell settings survive a local save and load round trip", async () =>
    {
        var expected = new AppSettings(
            DefaultProviderId: "chatgpt",
            GlobalShortcut: new GlobalShortcutSettings(
                true,
                GlobalShortcutModifiers.Control | GlobalShortcutModifiers.Alt,
                "Q"),
            LaunchOnStartup: true,
            CloseToTray: false,
            AlwaysOnTop: true,
            WindowPlacement: new WindowPlacementSnapshot(120, 80, 1100, 720));

        await WorkspaceSessionStore.SaveSettingsAsync(expected);
        await WorkspaceSessionStore.FlushWritesAsync();
        var actual = await WorkspaceSessionStore.LoadSettingsAsync();

        Equal(expected.DefaultProviderId, actual.DefaultProviderId);
        Equal(expected.GlobalShortcut, actual.GlobalShortcut);
        Equal(expected.LaunchOnStartup, actual.LaunchOnStartup);
        Equal(expected.CloseToTray, actual.CloseToTray);
        Equal(expected.AlwaysOnTop, actual.AlwaysOnTop);
        Equal(expected.WindowPlacement, actual.WindowPlacement);
    });

    await CheckAsync("provider catalog keeps a safe and data-driven contract", () =>
    {
        var providers = ProviderCatalog.AvailableProviders;
        Equal(8, providers.Count);
        Equal(providers.Count, providers.Select(provider => provider.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        True(providers.All(provider => provider.HomeUri.Scheme == Uri.UriSchemeHttps));
        True(providers.All(provider => provider.AppDomains.Contains(provider.HomeUri.IdnHost)));
        True(providers.All(provider => provider.AppDomains.Count > 0));
        True(providers.All(provider => provider.CompatibilityStatus is not "Verified"));
        True(providers.All(provider => provider.ProfileName.StartsWith("provider-", StringComparison.Ordinal)));
        return Task.CompletedTask;
    });

    if (runUiChecks)
    {
        await CheckAsync("a saved restricted locator restores its native workspace and isolated profile in a new application process", async () =>
        {
            await DeleteDirectoryWhenReleasedAsync(appDataRoot);
            var provider = ProviderCatalog.AvailableProviders.Single(candidate => candidate.Id == "chatgpt");
            var workspace = new WorkspaceTab(
                "workspace-restore",
                "ChatGPT",
                provider,
                provider.Id,
                keepActive: false,
                new Uri("https://chatgpt.com/c/opaque-restart-validation-id"),
                wasRestoredFromSession: false);
            var store = new WorkspaceSessionStore();
            await store.SaveSessionAsync([workspace], workspace.Id, restoreExactWorkspace: true);

            var appPath = GetAppPath();
            True(File.Exists(appPath));
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(appPath)
                {
                    WorkingDirectory = Path.GetDirectoryName(appPath)!,
                    UseShellExecute = false
                }
            };
            process.StartInfo.Environment["AI_DRAWER_TEST_DATA_ROOT"] = testRoot;
            True(process.Start());
            try
            {
                await WaitUntilAsync(() =>
                {
                    process.Refresh();
                    return process.MainWindowHandle != IntPtr.Zero;
                }, TimeSpan.FromSeconds(30), "AI Drawer main window");
                WindowSizingProbe.AssertMinimumTrackSize(process.MainWindowHandle);

                var root = AutomationElement.FromHandle(process.MainWindowHandle);
                var continueButton = FindByName(root, "Continue");
                if (continueButton is null)
                {
                    throw new InvalidOperationException("The isolated first-run welcome action was not found.");
                }

                await CompleteWelcomeAsync(root);
                await WaitUntilAsync(
                    () => FindByName(root, "ChatGPT") is not null,
                    TimeSpan.FromSeconds(15),
                    "restored ChatGPT workspace");
                try
                {
                    await WaitUntilAsync(
                        () => Directory.Exists(Path.Combine(appDataRoot, "WebView2")),
                        TimeSpan.FromSeconds(30),
                        "isolated WebView2 profile creation");
                }
                catch (TimeoutException exception)
                {
                    var nativeStatus = FindByAutomationId(root, "StatusMessage")?.Current.Name;
                    var existingDirectories = Directory.Exists(appDataRoot)
                        ? string.Join(", ", Directory.GetDirectories(appDataRoot).Select(Path.GetFileName))
                        : "none";
                    throw new TimeoutException(
                        $"{exception.Message} Native status: {nativeStatus ?? "unavailable"}. App-data directories: {existingDirectories}.",
                        exception);
                }
            }
            finally
            {
                process.Refresh();
                if (!process.HasExited && string.Equals(process.MainModule?.FileName, appPath, StringComparison.OrdinalIgnoreCase))
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
        });

        await CheckAsync("MVP shell controls apply and persist through the settings UI", async () =>
        {
            await DeleteDirectoryWhenReleasedAsync(appDataRoot);
            await WorkspaceSessionStore.SaveSettingsAsync(new AppSettings(
                OnboardingVersion: 2,
                GlobalShortcut: new GlobalShortcutSettings(Enabled: false),
                CloseToTray: true,
                AlwaysOnTop: false));
            await WorkspaceSessionStore.FlushWritesAsync();
            var store = new WorkspaceSessionStore();
            await store.SaveSessionAsync([new WorkspaceTab(1)], "workspace-1", restoreExactWorkspace: false);

            var appPath = GetAppPath();
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(appPath)
                {
                    WorkingDirectory = Path.GetDirectoryName(appPath)!,
                    UseShellExecute = false
                }
            };
            process.StartInfo.Environment["AI_DRAWER_TEST_DATA_ROOT"] = testRoot;
            True(process.Start());
            try
            {
                await WaitUntilAsync(() =>
                {
                    process.Refresh();
                    return process.MainWindowHandle != IntPtr.Zero;
                }, TimeSpan.FromSeconds(30), "AI Drawer settings window");

                var root = AutomationElement.FromHandle(process.MainWindowHandle);
                var settingsButton = FindByName(root, "Settings")
                    ?? throw new InvalidOperationException("Settings action was not found.");
                ((InvokePattern)settingsButton.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                await WaitUntilAsync(
                    () => FindByName(root, "Default provider") is not null,
                    TimeSpan.FromSeconds(10),
                    "MVP settings controls");

                True(FindByName(root, "Global shortcut") is not null);
                True(FindByName(root, "Launch on startup") is not null);
                True(FindByName(root, "Clear provider cache") is not null);
                True(FindByName(root, "Reset all AI website data") is not null);

                ToggleByName(root, "Always on top", ToggleState.On);
                ToggleByName(root, "Close button behavior", ToggleState.Off);
                await WaitUntilAsync(
                    () => TryReadSettings(settingsPath) is { AlwaysOnTop: true, CloseToTray: false },
                    TimeSpan.FromSeconds(10),
                    "persisted shell settings");

                True(process.CloseMainWindow());
                await WaitUntilAsync(() =>
                {
                    process.Refresh();
                    return process.HasExited;
                }, TimeSpan.FromSeconds(15), "close-to-exit behavior");
            }
            finally
            {
                process.Refresh();
                if (!process.HasExited && string.Equals(process.MainModule?.FileName, appPath, StringComparison.OrdinalIgnoreCase))
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
        });
    }

    await CheckAsync("navigation policy preserves reviewed origins and fails closed", () =>
    {
        var chatGpt = ProviderCatalog.AvailableProviders.Single(candidate => candidate.Id == "chatgpt");
        Equal(NavigationDisposition.EmbedProviderApplication, chatGpt.ClassifyTopLevelNavigation("https://chatgpt.com/c/opaque-id"));
        Equal(NavigationDisposition.EmbedAuthentication, chatGpt.ClassifyTopLevelNavigation("https://auth.openai.com/authorize"));
        Equal(NavigationDisposition.OpenExternal, chatGpt.ClassifyTopLevelNavigation("https://preview.chatgpt.com/c/opaque-id"));
        True(chatGpt.IsAuthenticationUri("https://auth.openai.com/authorize"));
        False(chatGpt.IsAuthenticationUri("https://chatgpt.com/c/opaque-id"));
        False(chatGpt.IsProviderAppUri("https://preview.chatgpt.com/c/opaque-id"));
        Equal(NavigationDisposition.OpenExternal, chatGpt.ClassifyTopLevelNavigation("https://chatgpt.com.evil.example/path?opaque=secret"));
        Equal(NavigationDisposition.BlockUnsupported, chatGpt.ClassifyTopLevelNavigation("https://user@chatgpt.com/"));
        Equal(NavigationDisposition.BlockUnsupported, chatGpt.ClassifyTopLevelNavigation("https://chatgpt.com:444/"));
        Equal(NavigationDisposition.BlockUnsupported, chatGpt.ClassifyTopLevelNavigation("http://chatgpt.com/"));
        Equal(PopupDisposition.OpenControlledProviderWindow, chatGpt.ClassifyPopup("https://chatgpt.com/c/opaque-id"));
        Equal(PopupDisposition.OpenControlledAuthenticationWindow, chatGpt.ClassifyPopup("https://auth.openai.com/authorize"));
        Equal(PopupDisposition.OpenExternal, chatGpt.ClassifyPopup("https://example.com/path?opaque=secret#fragment"));
        Equal(PopupDisposition.BlockUnsupported, chatGpt.ClassifyPopup("javascript:alert(1)"));
        Equal("https://example.com/path", ProviderDefinition.CreateSafeExternalUri("https://example.com/path?opaque=secret#fragment")?.AbsoluteUri.TrimEnd('/'));
        return Task.CompletedTask;
    });

    await CheckAsync("known purchase routes are blocked before navigation", () =>
    {
        var gemini = ProviderCatalog.AvailableProviders.Single(candidate => candidate.Id == "gemini");
        Equal(NavigationDisposition.BlockPurchase, gemini.ClassifyTopLevelNavigation("https://gemini.google.com/upgrade?opaque=secret"));
        Equal(PopupDisposition.BlockPurchase, gemini.ClassifyPopup("https://gemini.google.com/upgrade?opaque=secret"));
        True(gemini.IsKnownPurchaseUri("https://pay.google.com/checkout"));
        False(gemini.IsKnownPurchaseUri("http://pay.google.com/checkout"));
        return Task.CompletedTask;
    });

    if (runUiChecks)
    {
        await CheckAsync("the recovery UI backs up a corrupt session before continuing", async () =>
        {
            const string corruptSession = "{ session is broken";
            await DeleteDirectoryWhenReleasedAsync(appDataRoot);
            await WriteSessionAsync(corruptSession);
            var appPath = GetAppPath();
            True(File.Exists(appPath));

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(appPath)
                {
                    WorkingDirectory = Path.GetDirectoryName(appPath)!,
                    UseShellExecute = false
                }
            };
            process.StartInfo.Environment["AI_DRAWER_TEST_DATA_ROOT"] = testRoot;
            True(process.Start());
            try
            {
                await WaitUntilAsync(() =>
                {
                    process.Refresh();
                    return process.MainWindowHandle != IntPtr.Zero;
                }, TimeSpan.FromSeconds(30), "AI Drawer recovery window");

                var root = AutomationElement.FromHandle(process.MainWindowHandle);
                var title = FindByName(root, "Previous workspace session needs recovery");
                var backup = FindByName(root, "Back up and continue");
                var retry = FindByName(root, "Retry");
                True(title is not null);
                True(backup is { Current.IsEnabled: true });
                True(retry is { Current.IsEnabled: true });
                if (backup is null)
                {
                    throw new InvalidOperationException("Recovery backup action was not found.");
                }

                ((InvokePattern)backup.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                await WaitUntilAsync(
                    () => Directory.GetFiles(appDataRoot, "*.recovery-backup.json").Length == 1,
                    TimeSpan.FromSeconds(10),
                    "recovery backup creation");
                Equal(corruptSession, await File.ReadAllTextAsync(Directory.GetFiles(appDataRoot, "*.recovery-backup.json").Single()));
            }
            finally
            {
                process.Refresh();
                if (!process.HasExited && string.Equals(process.MainModule?.FileName, appPath, StringComparison.OrdinalIgnoreCase))
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
        });
    }

}
finally
{
    if (Directory.Exists(testRoot))
    {
        await DeleteDirectoryWhenReleasedAsync(testRoot);
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine($"{completedCheckCount} app session-recovery, policy, and UI boundary checks passed.");
return 0;

async Task WriteSessionAsync(string content)
{
    Directory.CreateDirectory(appDataRoot);
    await File.WriteAllTextAsync(sessionPath, content);
}

async Task CheckAsync(string name, Func<Task> test)
{
    try
    {
        await test();
        completedCheckCount++;
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL: {name}: {exception.Message}");
    }
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"expected {expected}, got {actual}");
    }
}

static void True(bool value)
{
    if (!value)
    {
        throw new InvalidOperationException("expected true");
    }
}

static void False(bool value) => True(!value);

static void Null(object? value)
{
    if (value is not null)
    {
        throw new InvalidOperationException($"expected null, got {value}");
    }
}

static async Task ThrowsAsync<TException>(Func<Task> action)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"expected {typeof(TException).Name}");
}

static AutomationElement? FindByName(AutomationElement root, string name)
{
    var condition = new PropertyCondition(AutomationElement.NameProperty, name);
    return root.FindFirst(TreeScope.Descendants, condition);
}

static AutomationElement? FindByAutomationId(AutomationElement root, string automationId)
{
    var condition = new PropertyCondition(AutomationElement.AutomationIdProperty, automationId);
    return root.FindFirst(TreeScope.Descendants, condition);
}

static void ToggleByName(AutomationElement root, string name, ToggleState expectedState)
{
    var element = FindByName(root, name)
        ?? throw new InvalidOperationException($"{name} toggle was not found.");
    var pattern = (TogglePattern)element.GetCurrentPattern(TogglePattern.Pattern);
    if (pattern.Current.ToggleState != expectedState)
    {
        pattern.Toggle();
    }
}

static AppSettings? TryReadSettings(string path)
{
    try
    {
        return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
    }
    catch
    {
        return null;
    }
}

static async Task CompleteWelcomeAsync(AutomationElement root)
{
    const int maximumDisclosureCount = 5;
    for (var index = 0; index < maximumDisclosureCount; index++)
    {
        var continueButton = FindByName(root, "Continue");
        if (continueButton is null)
        {
            return;
        }

        var currentTitle = FindByAutomationId(root, "PromptTitle")?.Current.Name;
        ((InvokePattern)continueButton.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        await WaitUntilAsync(
            () =>
            {
                var nextTitle = FindByAutomationId(root, "PromptTitle")?.Current.Name;
                return !string.Equals(nextTitle, currentTitle, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5),
            "welcome disclosure advance");
    }

    if (FindByName(root, "Continue") is not null)
    {
        throw new InvalidOperationException("The welcome flow exceeded the supported disclosure count.");
    }
}

static string GetAppPath() => Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory,
    "..", "..", "..", "..", "..", "..", "..",
    "src", "AIDrawer.App", "bin", "x64", "Debug",
    "net10.0-windows10.0.26100.0", "win-x64", "AIDrawer.App.exe"));

static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string description)
{
    var deadline = DateTime.UtcNow + timeout;
    while (!condition())
    {
        if (DateTime.UtcNow >= deadline)
        {
            throw new TimeoutException($"{description} was not ready within {timeout}.");
        }

        await Task.Delay(100);
    }
}

static async Task DeleteDirectoryWhenReleasedAsync(string path)
{
    const int maximumAttempts = 50;
    for (var attempt = 1; attempt <= maximumAttempts; attempt++)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }

            return;
        }
        catch (IOException) when (attempt < maximumAttempts)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        catch (UnauthorizedAccessException) when (attempt < maximumAttempts)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }

    throw new IOException($"Could not remove test directory after {maximumAttempts} attempts: {path}");
}

internal static class WindowSizingProbe
{
    private const uint WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 0x00000002;

    internal static void AssertMinimumTrackSize(IntPtr windowHandle)
    {
        var expectedWidth = WindowPlacementPolicy.MinimumWidth;
        var expectedHeight = WindowPlacementPolicy.MinimumHeight;
        var monitor = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref monitorInfo))
        {
            expectedWidth = Math.Min(expectedWidth, monitorInfo.WorkArea.Width);
            expectedHeight = Math.Min(expectedHeight, monitorInfo.WorkArea.Height);
        }

        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<MinMaxInfo>());
        try
        {
            Marshal.StructureToPtr(new MinMaxInfo(), pointer, false);
            _ = SendMessage(windowHandle, WmGetMinMaxInfo, IntPtr.Zero, pointer);
            var limits = Marshal.PtrToStructure<MinMaxInfo>(pointer);
            if (limits.MinimumTrackSize.X < expectedWidth
                || limits.MinimumTrackSize.Y < expectedHeight)
            {
                throw new InvalidOperationException(
                    $"expected minimum window size {expectedWidth}x{expectedHeight}, "
                    + $"got {limits.MinimumTrackSize.X}x{limits.MinimumTrackSize.Y}");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;

        internal readonly int Width => Right - Left;

        internal readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        internal NativePoint Reserved;
        internal NativePoint MaximumSize;
        internal NativePoint MaximumPosition;
        internal NativePoint MinimumTrackSize;
        internal NativePoint MaximumTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        internal uint Size;
        internal NativeRect MonitorArea;
        internal NativeRect WorkArea;
        internal uint Flags;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);
}
