using AIDrawer;
using AIDrawer.Core;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;
using System.Xml.Linq;

var failures = new List<string>();
var runUiChecks = !args.Contains("--no-ui", StringComparer.OrdinalIgnoreCase);
var runLiveExternalUriChecks = args.Contains("--live-external-uri", StringComparer.OrdinalIgnoreCase);
var completedCheckCount = 0;
var testRoot = Path.Combine(Path.GetTempPath(), $"AI-Drawer-SessionTests-{Guid.NewGuid():N}");
Environment.SetEnvironmentVariable("AI_DRAWER_TEST_DATA_ROOT", testRoot);
var appDataRoot = Path.Combine(testRoot, "AI Drawer");
var sessionPath = Path.Combine(appDataRoot, "workspaces-v1.json");
var settingsPath = Path.Combine(appDataRoot, "settings-v1.json");

if (!string.Equals(ApplicationDataPaths.AppDataRoot, appDataRoot, StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine(
        "The application test harness requires the Debug-only isolated data root and will not access normal application data.");
    return 2;
}

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
        Equal(9, providers.Count);
        Equal(providers.Count, providers.Select(provider => provider.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        True(providers.All(provider => provider.HomeUri.Scheme == Uri.UriSchemeHttps));
        True(providers.All(provider => provider.AppDomains.Contains(provider.HomeUri.IdnHost)));
        True(providers.All(provider => provider.AppDomains.Count > 0));
        True(providers.All(provider => provider.CompatibilityStatus is not "Verified"));
        True(providers.All(provider => provider.ProfileName.StartsWith("provider-", StringComparison.Ordinal)));
        return Task.CompletedTask;
    });

    await CheckAsync("isolated provider-origin overrides are debug-only and tightly bounded", () =>
    {
        var catalogCode = File.ReadAllText(GetRepositoryPath("src", "AIDrawer.App", "ProviderCatalog.cs"));
        True(catalogCode.Contains("#if DEBUG", StringComparison.Ordinal));
        True(catalogCode.Contains("AI_DRAWER_TEST_DATA_ROOT", StringComparison.Ordinal));
        True(catalogCode.Contains("AI_DRAWER_TEST_PROVIDER_ORIGIN", StringComparison.Ordinal));
        True(catalogCode.Contains("uri.IsDefaultPort", StringComparison.Ordinal));
        True(catalogCode.Contains("uri.AbsolutePath == \"/\"", StringComparison.Ordinal));
        return Task.CompletedTask;
    });

    await CheckAsync("isolated profile actions remain debug-only and path-scoped", () =>
    {
        var programCode = File.ReadAllText(GetRepositoryPath("src", "AIDrawer.App", "Program.cs"));
        var appCode = File.ReadAllText(GetRepositoryPath("src", "AIDrawer.App", "App.xaml.cs"));
        True(programCode.Contains("#if DEBUG", StringComparison.Ordinal));
        True(programCode.Contains("profile-action.acceptance", StringComparison.Ordinal));
        True(programCode.Contains("Path.GetFullPath(Path.GetTempPath())", StringComparison.Ordinal));
        True(programCode.Contains("AI-Drawer-RuntimeAcceptance-", StringComparison.Ordinal));
        True(programCode.Contains("new FileInfo(markerPath).Length > 32", StringComparison.Ordinal));
        True(programCode.Contains("clear-cache\" or \"reset-provider\" or \"reset-all", StringComparison.Ordinal));
        True(programCode.Contains("App.RunProfileActionForAcceptance(action, testDataRoot!);", StringComparison.Ordinal));
        True(appCode.Contains("internal static void RunProfileActionForAcceptance", StringComparison.Ordinal));
        False(AIDrawer.Program.IsIsolatedRuntimeAcceptanceRoot(@"C:\real-data"));
        var acceptanceRoot = Path.Combine(
            Path.GetTempPath(),
            $"AI-Drawer-RuntimeAcceptance-{Guid.NewGuid():N}");
        Directory.CreateDirectory(acceptanceRoot);
        try
        {
            True(AIDrawer.Program.IsIsolatedRuntimeAcceptanceRoot(acceptanceRoot));
        }
        finally
        {
            Directory.Delete(acceptanceRoot);
        }
        return Task.CompletedTask;
    });

    await CheckAsync("download controller treats files and directories as collisions and never launches results", () =>
    {
        var controllerCode = File.ReadAllText(GetRepositoryPath("src", "AIDrawer.App", "WebViewDownloadController.cs"));
        True(controllerCode.Contains("File.Exists(path) || Directory.Exists(path)", StringComparison.Ordinal));
        True(controllerCode.Contains("args.Cancel = !decision.Allowed", StringComparison.Ordinal));
        False(controllerCode.Contains("Launcher.Launch", StringComparison.Ordinal));
        False(controllerCode.Contains("Process.Start", StringComparison.Ordinal));
        return Task.CompletedTask;
    });

    await CheckAsync("Copilot embeds only exact reviewed personal application origins", () =>
    {
        var copilot = ProviderCatalog.AvailableProviders.Single(candidate => candidate.Id == "copilot");
        Equal("Microsoft Copilot (Personal)", copilot.DisplayName);
        Equal("Experimental", copilot.CompatibilityStatus);
        Equal("https://copilot.microsoft.com/", copilot.HomeUri.AbsoluteUri);
        Equal(2, copilot.AppDomains.Count);
        True(copilot.AppDomains.Contains("copilot.microsoft.com"));
        True(copilot.AppDomains.Contains("copilot.com"));
        Equal(0, copilot.AuthenticationDomains.Count);
        Equal(0, copilot.RestorePathPrefixes.Count);

        Equal(NavigationDisposition.EmbedProviderApplication, copilot.ClassifyTopLevelNavigation("https://copilot.microsoft.com/"));
        Equal(NavigationDisposition.EmbedProviderApplication, copilot.ClassifyTopLevelNavigation("https://copilot.com/"));
        Equal(NavigationDisposition.OpenExternal, copilot.ClassifyTopLevelNavigation("https://www.copilot.com/"));
        Equal(NavigationDisposition.OpenExternal, copilot.ClassifyTopLevelNavigation("https://copilot.cloud.microsoft/"));
        Equal(NavigationDisposition.OpenExternal, copilot.ClassifyTopLevelNavigation("https://copilot.microsoft.com.evil.example/"));
        Equal(NavigationDisposition.OpenExternal, copilot.ClassifyTopLevelNavigation("https://login.live.com/"));
        Equal(NavigationDisposition.BlockUnsupported, copilot.ClassifyTopLevelNavigation("http://copilot.microsoft.com/"));
        Equal(NavigationDisposition.BlockUnsupported, copilot.ClassifyTopLevelNavigation("https://user@copilot.microsoft.com/"));
        Equal(NavigationDisposition.BlockUnsupported, copilot.ClassifyTopLevelNavigation("https://copilot.microsoft.com:444/"));
        Null(copilot.CreateRestoreLocator("https://copilot.microsoft.com/chats/opaque-id"));
        return Task.CompletedTask;
    });

    await CheckAsync("isolated data roots use isolated non-disclosing single-instance keys", () =>
    {
        Equal("AI Drawer", AIDrawer.Program.ResolveSingleInstanceKey(null));
        Equal("AI Drawer", AIDrawer.Program.ResolveSingleInstanceKey("relative-test-root"));

        var firstKey = AIDrawer.Program.ResolveSingleInstanceKey(@"C:\tmp\AI Drawer Tests\first");
        var equivalentFirstKey = AIDrawer.Program.ResolveSingleInstanceKey(@"c:\TMP\AI Drawer Tests\first\");
        var secondKey = AIDrawer.Program.ResolveSingleInstanceKey(@"C:\tmp\AI Drawer Tests\second");
        Equal(firstKey, equivalentFirstKey);
        False(string.Equals(firstKey, secondKey, StringComparison.Ordinal));
        False(firstKey.Contains("AI Drawer Tests", StringComparison.OrdinalIgnoreCase));
        True(firstKey.StartsWith("AI Drawer Test ", StringComparison.Ordinal));
        True(AIDrawer.Program.ResolveStartupActivation(
            Microsoft.Windows.AppLifecycle.ExtendedActivationKind.StartupTask,
            testDataRoot: null));
        False(AIDrawer.Program.ResolveStartupActivation(
            Microsoft.Windows.AppLifecycle.ExtendedActivationKind.StartupTask,
            @"C:\tmp\AI Drawer Tests\foreground"));
        False(AIDrawer.Program.ResolveStartupActivation(
            Microsoft.Windows.AppLifecycle.ExtendedActivationKind.Launch,
            testDataRoot: null));
        return Task.CompletedTask;
    });

    await CheckAsync("prompt card avoids independent translation", () =>
    {
        var document = XDocument.Load(GetRepositoryPath("src", "AIDrawer.App", "MainPage.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var promptCard = document
            .Descendants()
            .Single(element => string.Equals(
                element.Attribute(xaml + "Name")?.Value,
                "PromptCard",
                StringComparison.Ordinal));

        Null(promptCard.Attribute("Translation"));
        return Task.CompletedTask;
    });

    await CheckAsync("home provider choices expose complete shortcuts and scoped hover feedback", () =>
    {
        var document = XDocument.Load(GetRepositoryPath("src", "AIDrawer.App", "MainPage.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var providerChooser = document
            .Descendants()
            .Single(element => string.Equals(
                element.Attribute(xaml + "Name")?.Value,
                "ProviderChooser",
                StringComparison.Ordinal));
        var expectedResourcesByTheme = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["Light"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ButtonBackgroundPointerOver"] = "ProviderChoiceBackgroundPointerOverBrush",
                ["ButtonBackgroundPressed"] = "ProviderChoiceBackgroundPressedBrush",
                ["ButtonBorderBrushPointerOver"] = "ProviderChoiceBorderPointerOverBrush",
                ["ButtonBorderBrushPressed"] = "ProviderChoiceBorderPressedBrush"
            },
            ["Dark"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ButtonBackgroundPointerOver"] = "ProviderChoiceBackgroundPointerOverBrush",
                ["ButtonBackgroundPressed"] = "ProviderChoiceBackgroundPressedBrush",
                ["ButtonBorderBrushPointerOver"] = "ProviderChoiceBorderPointerOverBrush",
                ["ButtonBorderBrushPressed"] = "ProviderChoiceBorderPressedBrush"
            },
            ["HighContrast"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ButtonBackgroundPointerOver"] = "ControlFillColorSecondaryBrush",
                ["ButtonBackgroundPressed"] = "ControlFillColorTertiaryBrush",
                ["ButtonBorderBrushPointerOver"] = "ControlStrokeColorDefaultBrush",
                ["ButtonBorderBrushPressed"] = "ControlStrongStrokeColorDefaultBrush"
            }
        };
        foreach (var (theme, expectedResources) in expectedResourcesByTheme)
        {
            var themeDictionary = providerChooser
                .Descendants()
                .Single(element => string.Equals(element.Name.LocalName, "ResourceDictionary", StringComparison.Ordinal)
                    && string.Equals(element.Attribute(xaml + "Key")?.Value, theme, StringComparison.Ordinal));
            foreach (var (resourceKey, expectedTarget) in expectedResources)
            {
                var resource = themeDictionary
                    .Descendants()
                    .Single(element => string.Equals(element.Attribute(xaml + "Key")?.Value, resourceKey, StringComparison.Ordinal));
                Equal(expectedTarget, resource.Attribute("ResourceKey")?.Value);
            }
        }

        var providerStyle = document
            .Descendants()
            .Single(element => string.Equals(
                element.Attribute(xaml + "Key")?.Value,
                "ProviderChoiceButtonStyle",
                StringComparison.Ordinal));
        True(providerStyle
            .Descendants()
            .Any(element => string.Equals(element.Attribute("Property")?.Value, "BorderThickness", StringComparison.Ordinal)
                && string.Equals(element.Attribute("Value")?.Value, "1", StringComparison.Ordinal)));
        False(providerStyle
            .Descendants()
            .Any(element => element.Attribute("Property")?.Value is "Translation" or "Shadow"));

        True(document
            .Descendants()
            .Any(element => string.Equals(element.Name.LocalName, "KeyboardAccelerator", StringComparison.Ordinal)
                && string.Equals(element.Attribute("Key")?.Value, "Number9", StringComparison.Ordinal)
                && string.Equals(element.Attribute("Modifiers")?.Value, "Control", StringComparison.Ordinal)
                && string.Equals(element.Attribute("Invoked")?.Value, "ProviderShortcut_Invoked", StringComparison.Ordinal)));
        True(document
            .Descendants()
            .Any(element => string.Equals(element.Attribute("Text")?.Value, "Ctrl+1–9", StringComparison.Ordinal)));
        True(document
            .Descendants()
            .Any(element => string.Equals(element.Attribute("SizeChanged")?.Value, "ProviderChooser_SizeChanged", StringComparison.Ordinal)));
        var missingProviderFeedback = document
            .Descendants()
            .Single(element => string.Equals(
                element.Attribute(xaml + "Name")?.Value,
                "MissingProviderFeedbackButton",
                StringComparison.Ordinal));
        Equal("Can't find the AI you want? Tell me.", missingProviderFeedback.Attribute("AutomationProperties.Name")?.Value);
        Equal("MissingProviderFeedbackButton_Click", missingProviderFeedback.Attribute("Click")?.Value);
        var rootLayout = document
            .Descendants()
            .Single(element => string.Equals(
                element.Attribute(xaml + "Name")?.Value,
                "RootLayout",
                StringComparison.Ordinal));
        Equal("RootLayout_SizeChanged", rootLayout.Attribute("SizeChanged")?.Value);
        var homeContentContainer = document
            .Descendants()
            .Single(element => string.Equals(
                element.Attribute(xaml + "Name")?.Value,
                "HomeContentContainer",
                StringComparison.Ordinal));
        Equal("688", homeContentContainer.Attribute("MaxWidth")?.Value);
        Null(homeContentContainer.Attribute("Width"));
        var homeTitle = document
            .Descendants()
            .Single(element => string.Equals(
                element.Attribute("Text")?.Value,
                "Where do you want to work?",
                StringComparison.Ordinal));
        Equal("Center", homeTitle.Attribute("TextAlignment")?.Value);
        Equal("WrapWholeWords", homeTitle.Attribute("TextWrapping")?.Value);
        var supportActions = document
            .Descendants()
            .Single(element => string.Equals(
                element.Attribute(xaml + "Name")?.Value,
                "HomeSupportActions",
                StringComparison.Ordinal));
        Equal("HomeSupportActions_SizeChanged", supportActions.Attribute("SizeChanged")?.Value);
        Equal(1, MainPage.ResolveProviderChooserColumnCount(539));
        Equal(2, MainPage.ResolveProviderChooserColumnCount(540));
        Equal(2, MainPage.ResolveProviderChooserColumnCount(688));

        var appProject = File.ReadAllText(GetRepositoryPath("src", "AIDrawer.App", "AIDrawer.App.csproj"));
        False(appProject.Contains("Assets\\Providers", StringComparison.Ordinal));
        False(Directory.Exists(GetRepositoryPath("src", "AIDrawer.App", "Assets", "Providers")));

        var mainPageCode = File.ReadAllText(GetRepositoryPath("src", "AIDrawer.App", "MainPage.xaml.cs"));
        False(mainPageCode.Contains("CreateProviderMark", StringComparison.Ordinal));
        True(mainPageCode.Contains("Keyboard shortcut Ctrl+", StringComparison.Ordinal));
        True(mainPageCode.Contains("Glyph = \"\\uE76C\"", StringComparison.Ordinal));
        True(mainPageCode.Contains("VirtualKey.Number9 => 8", StringComparison.Ordinal));
        True(mainPageCode.Contains("https://forms.cloud.microsoft/r/WLQySVad7g", StringComparison.Ordinal));
        True(mainPageCode.Contains(
            "HomeContentContainer.Width = Math.Min(args.NewSize.Width, HomeContentMaxWidth);",
            StringComparison.Ordinal));
        return Task.CompletedTask;
    });

    await CheckAsync("native overlays and support surfaces preserve accessibility boundaries", () =>
    {
        var document = XDocument.Load(GetRepositoryPath("src", "AIDrawer.App", "MainPage.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var rootThemeDictionaries = document
            .Descendants()
            .First(element => string.Equals(element.Name.LocalName, "ResourceDictionary.ThemeDictionaries", StringComparison.Ordinal));
        True(rootThemeDictionaries
            .Elements()
            .Any(element => string.Equals(element.Attribute(xaml + "Key")?.Value, "HighContrast", StringComparison.Ordinal)));

        foreach (var overlayName in new[]
        {
            "RecoveryPanel",
            "SessionRecoveryOverlay",
            "PromptOverlay",
            "FeedbackSupportOverlay",
            "SettingsOverlay"
        })
        {
            var overlay = document
                .Descendants()
                .Single(element => string.Equals(element.Attribute(xaml + "Name")?.Value, overlayName, StringComparison.Ordinal));
            Equal("Cycle", overlay.Attribute("TabFocusNavigation")?.Value);
        }

        var keepWaiting = document
            .Descendants()
            .Single(element => string.Equals(element.Attribute(xaml + "Name")?.Value, "RecoveryKeepWaitingButton", StringComparison.Ordinal));
        Equal("Keep waiting", keepWaiting.Attribute("Content")?.Value);
        Equal("RecoveryKeepWaitingButton_Click", keepWaiting.Attribute("Click")?.Value);
        var openRecoveryInBrowser = document
            .Descendants()
            .Single(element => string.Equals(element.Attribute(xaml + "Name")?.Value, "RecoveryOpenBrowserButton", StringComparison.Ordinal));
        Equal("Open in browser", openRecoveryInBrowser.Attribute("Content")?.Value);
        Equal("RecoveryOpenBrowserButton_Click", openRecoveryInBrowser.Attribute("Click")?.Value);

        var feedbackButton = document
            .Descendants()
            .Single(element => string.Equals(element.Attribute(xaml + "Name")?.Value, "FeedbackSupportButton", StringComparison.Ordinal));
        Equal("Feedback and support", feedbackButton.Attribute("AutomationProperties.Name")?.Value);
        var feedbackOverlay = document
            .Descendants()
            .Single(element => string.Equals(element.Attribute(xaml + "Name")?.Value, "FeedbackSupportOverlay", StringComparison.Ordinal));
        foreach (var requiredAction in new[] { "ReportBugButton", "ReportProviderEvidenceButton", "ReportSecurityButton" })
        {
            True(feedbackOverlay
                .Descendants()
                .Any(element => string.Equals(element.Attribute(xaml + "Name")?.Value, requiredAction, StringComparison.Ordinal)));
        }

        True(feedbackOverlay
            .Descendants()
            .Any(element => string.Equals(element.Attribute("Text")?.Value, "Keep reports privacy-safe", StringComparison.Ordinal)));

        var settings = document
            .Descendants()
            .Single(element => string.Equals(element.Attribute(xaml + "Name")?.Value, "SettingsOverlay", StringComparison.Ordinal));
        True(settings
            .Descendants()
            .Any(element => string.Equals(element.Attribute(xaml + "Name")?.Value, "SupportDevelopmentButton", StringComparison.Ordinal)));

        var externalLauncherCode = File.ReadAllText(
            GetRepositoryPath("src", "AIDrawer.App", "ExternalUriLauncher.cs"));
        True(externalLauncherCode.Contains("#if DEBUG", StringComparison.Ordinal));
        True(externalLauncherCode.Contains("AI_DRAWER_TEST_RECORD_EXTERNAL_URI", StringComparison.Ordinal));
        True(externalLauncherCode.Contains("AI-Drawer-SessionTests-", StringComparison.Ordinal));
        True(externalLauncherCode.Contains("FileAttributes.ReparsePoint", StringComparison.Ordinal));
        True(externalLauncherCode.Contains("https://forms.cloud.microsoft/r/WLQySVad7g", StringComparison.Ordinal));
        True(externalLauncherCode.Contains("https://buymeacoffee.com/edward_lee", StringComparison.Ordinal));
        var mainPageCode = File.ReadAllText(GetRepositoryPath("src", "AIDrawer.App", "MainPage.xaml.cs"));
        True(mainPageCode.Contains("ExternalUriLauncher.LaunchAsync", StringComparison.Ordinal));
        False(mainPageCode.Contains("Launcher.LaunchUriAsync", StringComparison.Ordinal));
        return Task.CompletedTask;
    });

    await CheckAsync("beta release tooling and public copy remain explicit and verifiable", () =>
    {
        var appProject = File.ReadAllText(GetRepositoryPath("src", "AIDrawer.App", "AIDrawer.App.csproj"));
        False(appProject.Contains("<PublishProfile Condition=", StringComparison.Ordinal));

        var candidateBuilder = File.ReadAllText(GetRepositoryPath("tools", "Build-InternalBetaCandidate.ps1"));
        True(candidateBuilder.Contains("FrameworkDependent", StringComparison.Ordinal));
        True(candidateBuilder.Contains("SelfContained", StringComparison.Ordinal));
        True(candidateBuilder.Contains("WindowsAppSDKSelfContained", StringComparison.Ordinal));
        True(candidateBuilder.Contains("deliveryPackageBytes", StringComparison.Ordinal));
        True(candidateBuilder.Contains("candidateCompleted", StringComparison.Ordinal));

        var candidateVerifier = File.ReadAllText(GetRepositoryPath("tools", "Test-BetaCandidate.ps1"));
        True(candidateVerifier.Contains("Get-AuthenticodeSignature", StringComparison.Ordinal));
        True(candidateVerifier.Contains("DownloadedPackagePath", StringComparison.Ordinal));
        True(candidateVerifier.Contains("Candidate path escapes its directory", StringComparison.Ordinal));
        True(candidateVerifier.Contains("A public candidate must have clean source", StringComparison.Ordinal));

        var workflow = File.ReadAllText(
            GetRepositoryPath(".github", "workflows", "internal-beta-candidate.yml"));
        True(workflow.Contains("dependency_mode:", StringComparison.Ordinal));
        True(workflow.Contains("- FrameworkDependent", StringComparison.Ordinal));
        True(workflow.Contains("- SelfContained", StringComparison.Ordinal));

        var publicRoot = GetRepositoryPath("docs", "public");
        foreach (var fileName in new[]
        {
            "index.md",
            "download.md",
            "providers.md",
            "privacy.md",
            "security.md",
            "changelog.md",
            "support.md"
        })
        {
            True(File.Exists(Path.Combine(publicRoot, fileName)));
        }

        var privacyCopy = File.ReadAllText(Path.Combine(publicRoot, "privacy.md"));
        True(privacyCopy.Contains("does not read or store prompts", StringComparison.Ordinal));
        True(privacyCopy.Contains("WebView2 website data", StringComparison.Ordinal));
        var supportCopy = File.ReadAllText(Path.Combine(publicRoot, "support.md"));
        True(supportCopy.Contains("https://forms.cloud.microsoft/r/WLQySVad7g", StringComparison.Ordinal));
        True(supportCopy.Contains("security/advisories/new", StringComparison.Ordinal));
        var downloadCopy = File.ReadAllText(Path.Combine(publicRoot, "download.md"));
        True(downloadCopy.Contains("does not currently have a supported public build", StringComparison.Ordinal));
        return Task.CompletedTask;
    });

    await CheckAsync("compatibility lab defaults to a disposable fail-closed observation boundary", () =>
    {
        var document = XDocument.Load(GetRepositoryPath("src", "AIDock.CompatibilityLab", "MainPage.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var profileMode = document
            .Descendants()
            .Single(element => string.Equals(element.Attribute(xaml + "Name")?.Value, "ProfileModeBox", StringComparison.Ordinal));
        Equal("1", profileMode.Attribute("SelectedIndex")?.Value);
        var observationMode = document
            .Descendants()
            .Single(element => string.Equals(element.Attribute(xaml + "Name")?.Value, "ObservationModeBox", StringComparison.Ordinal));
        Equal("Provider observation mode", observationMode.Attribute("AutomationProperties.Name")?.Value);

        var code = File.ReadAllText(GetRepositoryPath("src", "AIDock.CompatibilityLab", "MainPage.xaml.cs"));
        True(code.Contains("CoreWebView2ServerCertificateErrorAction.Cancel", StringComparison.Ordinal));
        True(code.Contains("args.State = CoreWebView2PermissionState.Deny", StringComparison.Ordinal));
        True(code.Contains("args.Cancel = true;", StringComparison.Ordinal));
        True(code.Contains("Provider observation mode requires a fresh disposable profile.", StringComparison.Ordinal));
        return Task.CompletedTask;
    });

    await CheckAsync("browser recovery observes the environment exit and deduplicates with per-view failures", () =>
    {
        var coordinatorCode = File.ReadAllText(GetRepositoryPath("src", "AIDrawer.App", "WorkspaceCoordinator.cs"));
        True(coordinatorCode.Contains("environment.BrowserProcessExited += Environment_BrowserProcessExited", StringComparison.Ordinal));
        True(coordinatorCode.Contains("environment.BrowserProcessExited -= Environment_BrowserProcessExited", StringComparison.Ordinal));
        True(coordinatorCode.Contains("_browserRecoveryInProgress", StringComparison.Ordinal));
        True(coordinatorCode.Contains("_profileCleanupInProgress", StringComparison.Ordinal));
        True(coordinatorCode.Contains("RecoverBrowserProcessAfterExitFromQueueAsync", StringComparison.Ordinal));
        True(coordinatorCode.Contains("attempt < 50", StringComparison.Ordinal));
        True(coordinatorCode.Contains("Process.GetProcessById", StringComparison.Ordinal));
        True(coordinatorCode.Contains("process.EnableRaisingEvents = true", StringComparison.Ordinal));
        True(coordinatorCode.Contains("DetachObservedBrowserProcess", StringComparison.Ordinal));
        return Task.CompletedTask;
    });

    if (runUiChecks)
    {
        await CheckAsync("support reminder appears at seven eligible opens and persists both dismissal choices", async () =>
        {
            await DeleteDirectoryWhenReleasedAsync(appDataRoot);
            await WorkspaceSessionStore.SaveSettingsAsync(new AppSettings(
                OnboardingVersion: 2,
                FirstUsedUtc: DateTimeOffset.UtcNow.AddDays(-8),
                SuccessfulOpenCount: SupportReminderPolicy.SuccessfulOpenThreshold,
                GlobalShortcut: new GlobalShortcutSettings(Enabled: false),
                CloseToTray: false));
            await WorkspaceSessionStore.FlushWritesAsync();

            var appPath = GetAppPath();
            True(File.Exists(appPath));
            using (var process = StartTestApp(
                appPath,
                testRoot,
                recordExternalUris: true,
                launchExternalUris: runLiveExternalUriChecks))
            {
                try
                {
                    var root = await WaitForMainWindowAsync(process, "support reminder window");
                    var title = await WaitForVisibleElementAsync(root, "Support independent development", "support reminder");
                    var never = FindButtonByName(root, "Don't ask again")
                        ?? throw new InvalidOperationException("The support reminder's permanent dismissal was not found.");
                    var notNow = await WaitForVisibleButtonAsync(
                        root,
                        "Not now",
                        "support reminder Not now action");
                    if (title.Current.BoundingRectangle.Width <= 0)
                    {
                        throw new InvalidOperationException("The visible support reminder had no UI Automation bounds.");
                    }

                    if (!notNow.Current.IsKeyboardFocusable || !never.Current.IsKeyboardFocusable)
                    {
                        throw new InvalidOperationException(
                            $"Support dismissal actions were not both keyboard-focusable "
                            + $"(Not now={notNow.Current.IsKeyboardFocusable}, Don't ask again={never.Current.IsKeyboardFocusable}).");
                    }

                    if (Directory.Exists(Path.Combine(appDataRoot, "WebView2")))
                    {
                        throw new InvalidOperationException("The Home-only support reminder unexpectedly created a WebView2 profile.");
                    }

                    var launchLogPath = Path.Combine(testRoot, "external-uri-launches.acceptance");
                    var openSupport = await WaitForVisibleButtonAsync(
                        root,
                        "Open Buy Me a Coffee",
                        "Home Buy Me a Coffee action");
                    ((InvokePattern)openSupport.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                    await WaitForExternalUriCountAsync(launchLogPath, 1, "Home Buy Me a Coffee routing");

                    var missingProvider = await WaitForVisibleButtonAsync(
                        root,
                        "Can't find the AI you want? Tell me.",
                        "Home feedback action");
                    ((InvokePattern)missingProvider.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                    await WaitForExternalUriCountAsync(launchLogPath, 2, "Home feedback routing");

                    var feedback = await WaitForVisibleButtonAsync(
                        root,
                        "Feedback and support",
                        "Feedback and Support action");
                    ((InvokePattern)feedback.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                    var reportBug = await WaitForVisibleButtonAsync(
                        root,
                        "Send feedback or report a bug",
                        "Feedback form action");
                    ((InvokePattern)reportBug.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                    await WaitForExternalUriCountAsync(launchLogPath, 3, "Feedback surface routing");

                    var settings = await WaitForVisibleButtonAsync(root, "Settings", "Settings action");
                    ((InvokePattern)settings.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                    var settingsSupport = await WaitForVisibleButtonAsync(
                        root,
                        "Support Edward Lee's independent projects",
                        "Settings Buy Me a Coffee action");
                    ((InvokePattern)settingsSupport.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                    await WaitForExternalUriCountAsync(launchLogPath, 4, "Settings Buy Me a Coffee routing");

                    var recordedUris = File.ReadAllLines(launchLogPath);
                    Equal("https://buymeacoffee.com/edward_lee", recordedUris[0]);
                    Equal("https://forms.cloud.microsoft/r/WLQySVad7g", recordedUris[1]);
                    Equal("https://forms.cloud.microsoft/r/WLQySVad7g", recordedUris[2]);
                    Equal("https://buymeacoffee.com/edward_lee", recordedUris[3]);

                    var closeSettings = await WaitForVisibleButtonAsync(root, "Close settings", "Settings close action");
                    ((InvokePattern)closeSettings.GetCurrentPattern(InvokePattern.Pattern)).Invoke();

                    ((InvokePattern)notNow.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                    await WaitUntilAsync(
                        () => TryReadSettings(settingsPath) is
                        {
                            SupportReminderSnoozedUntilUtc: { } untilUtc,
                            SupportReminderSnoozedUntilMajorRelease: 2
                        }
                        && untilUtc >= DateTimeOffset.UtcNow.AddDays(89),
                        TimeSpan.FromSeconds(20),
                        "support reminder snooze persistence");
                    await WaitUntilAsync(
                        () => !IsVisibleByName(root, "Support independent development"),
                        TimeSpan.FromSeconds(10),
                        "support reminder snooze dismissal");
                }
                finally
                {
                    await StopTestAppAsync(process, appPath);
                }
            }

            await DeleteDirectoryWhenReleasedAsync(appDataRoot);
            await WorkspaceSessionStore.SaveSettingsAsync(new AppSettings(
                OnboardingVersion: 2,
                FirstUsedUtc: DateTimeOffset.UtcNow.AddDays(-8),
                SuccessfulOpenCount: SupportReminderPolicy.SuccessfulOpenThreshold,
                GlobalShortcut: new GlobalShortcutSettings(Enabled: false),
                CloseToTray: false));
            await WorkspaceSessionStore.FlushWritesAsync();

            using (var process = StartTestApp(appPath, testRoot))
            {
                try
                {
                    var root = await WaitForMainWindowAsync(process, "support dismissal window");
                    _ = await WaitForVisibleElementAsync(root, "Support independent development", "support reminder");
                    var never = await WaitForVisibleButtonAsync(
                        root,
                        "Don't ask again",
                        "support reminder permanent dismissal");
                    ((InvokePattern)never.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                    await WaitUntilAsync(
                        () => !IsVisibleByName(root, "Support independent development"),
                        TimeSpan.FromSeconds(10),
                        "permanent support reminder dismissal");
                    try
                    {
                        await WaitUntilAsync(
                            () => TryReadSettings(settingsPath) is { SupportReminderDismissed: true },
                            TimeSpan.FromSeconds(20),
                            "permanent support reminder persistence");
                    }
                    catch (TimeoutException exception)
                    {
                        var settingsSnapshot = File.Exists(settingsPath)
                            ? File.ReadAllText(settingsPath)
                            : "<missing>";
                        var statusSnapshot = FindByAutomationId(root, "StatusMessage")?.Current.Name ?? "<none>";
                        throw new TimeoutException(
                            $"{exception.Message} Settings: {settingsSnapshot}. Native status: {statusSnapshot}.",
                            exception);
                    }
                }
                finally
                {
                    await StopTestAppAsync(process, appPath);
                }
            }
        });

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
                var copilotChoice = FindByName(root, "Open Microsoft Copilot (Personal) workspace");
                True(copilotChoice is { Current.IsEnabled: true });
                True(copilotChoice?.Current.BoundingRectangle.Height >= 56);
                WindowSizingProbe.Resize(
                    process.MainWindowHandle,
                    WindowPlacementPolicy.MinimumWidth,
                    640);
                await WaitUntilAsync(
                    () => Math.Abs(root.Current.BoundingRectangle.Width - WindowPlacementPolicy.MinimumWidth) <= 2,
                    TimeSpan.FromSeconds(5),
                    "minimum-width provider layout");
                AssertProviderChoicesCenteredAndVisible(root);
                WindowSizingProbe.Resize(process.MainWindowHandle, 1180, 780);
                await WaitUntilAsync(
                    () => Math.Abs(root.Current.BoundingRectangle.Width - 1180) <= 2,
                    TimeSpan.FromSeconds(5),
                    "restored provider layout");
                AssertProviderChoicesCenteredAndVisible(root);
                var feedbackButton = await WaitForVisibleButtonAsync(
                    root,
                    "Feedback and support",
                    "feedback and support action");
                ((InvokePattern)feedbackButton.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                _ = await WaitForVisibleElementAsync(root, "Send feedback or report a bug", "feedback and support overlay");
                var providerEvidence = FindButtonByName(root, "Report provider compatibility")
                    ?? throw new InvalidOperationException("Provider evidence action was not found.");
                var privateSecurity = FindButtonByName(root, "Report a security issue privately")
                    ?? throw new InvalidOperationException("Private security action was not found.");
                if (!providerEvidence.Current.IsKeyboardFocusable || !privateSecurity.Current.IsKeyboardFocusable)
                {
                    throw new InvalidOperationException(
                        $"Feedback report actions were not both keyboard-focusable "
                        + $"(provider={providerEvidence.Current.IsKeyboardFocusable}, security={privateSecurity.Current.IsKeyboardFocusable}).");
                }

                if (FindByName(root, "Version and build information") is null)
                {
                    throw new InvalidOperationException("Version and build information was not exposed to UI Automation.");
                }

                if (Directory.Exists(Path.Combine(appDataRoot, "WebView2")))
                {
                    throw new InvalidOperationException("Opening native Feedback and Support unexpectedly created a WebView2 profile.");
                }
                var closeFeedback = FindByName(root, "Close feedback and support")
                    ?? throw new InvalidOperationException("Feedback and support close action was not found.");
                ((InvokePattern)closeFeedback.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                await WaitUntilAsync(
                    () => FindByName(root, "Send feedback or report a bug") is null,
                    TimeSpan.FromSeconds(5),
                    "feedback and support dismissal");
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
                await WaitUntilAsync(
                    () => TryReadSettings(settingsPath) is { AlwaysOnTop: true },
                    TimeSpan.FromSeconds(20),
                    "persisted always-on-top setting");
                ToggleByName(root, "Close button behavior", ToggleState.Off);
                try
                {
                    await WaitUntilAsync(
                        () => TryReadSettings(settingsPath) is { AlwaysOnTop: true, CloseToTray: false },
                        TimeSpan.FromSeconds(20),
                        "persisted close-button setting");
                }
                catch (TimeoutException exception)
                {
                    var settingsSnapshot = File.Exists(settingsPath)
                        ? File.ReadAllText(settingsPath)
                        : "<missing>";
                    var statusSnapshot = FindByAutomationId(root, "StatusMessage")?.Current.Name ?? "<none>";
                    throw new TimeoutException(
                        $"{exception.Message} Settings: {settingsSnapshot}. Native status: {statusSnapshot}.",
                        exception);
                }

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
        Equal(
            "https://chatgpt.com/c/opaque-id",
            chatGpt.CreateSafeInMemoryUri("https://chatgpt.com/c/opaque-id?token=secret#fragment")?.AbsoluteUri.TrimEnd('/'));
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
        var rootCause = exception;
        while (rootCause.InnerException is not null)
        {
            rootCause = rootCause.InnerException;
        }

        failures.Add(
            $"FAIL: {name}: {rootCause.GetType().Name}: {rootCause.Message}{Environment.NewLine}{rootCause.StackTrace}");
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

static bool IsVisibleByName(AutomationElement root, string name)
{
    var element = FindByName(root, name);
    if (element is null)
    {
        return false;
    }

    try
    {
        var bounds = element.Current.BoundingRectangle;
        return !element.Current.IsOffscreen && bounds.Width > 0 && bounds.Height > 0;
    }
    catch (ElementNotAvailableException)
    {
        return false;
    }
}

static AutomationElement? FindButtonByName(AutomationElement root, string name)
{
    var condition = new AndCondition(
        new PropertyCondition(AutomationElement.NameProperty, name),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
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

static Process StartTestApp(
    string appPath,
    string testDataRoot,
    bool recordExternalUris = false,
    bool launchExternalUris = false)
{
    var process = new Process
    {
        StartInfo = new ProcessStartInfo(appPath)
        {
            WorkingDirectory = Path.GetDirectoryName(appPath)!,
            UseShellExecute = false
        }
    };
    process.StartInfo.Environment["AI_DRAWER_TEST_DATA_ROOT"] = testDataRoot;
    if (recordExternalUris)
    {
        process.StartInfo.Environment["AI_DRAWER_TEST_RECORD_EXTERNAL_URI"] = "1";
    }
    if (launchExternalUris)
    {
        process.StartInfo.Environment["AI_DRAWER_TEST_LAUNCH_EXTERNAL_URI"] = "1";
    }

    if (!process.Start())
    {
        process.Dispose();
        throw new InvalidOperationException("AI Drawer did not start.");
    }

    return process;
}

static async Task<AutomationElement> WaitForMainWindowAsync(Process process, string description)
{
    await WaitUntilAsync(() =>
    {
        process.Refresh();
        return process.MainWindowHandle != IntPtr.Zero;
    }, TimeSpan.FromSeconds(30), description);
    return AutomationElement.FromHandle(process.MainWindowHandle);
}

static async Task<AutomationElement> WaitForVisibleElementAsync(
    AutomationElement root,
    string name,
    string description)
{
    AutomationElement? element = null;
    await WaitUntilAsync(
        () =>
        {
            element = FindByName(root, name);
            return element is not null
                && !element.Current.IsOffscreen
                && element.Current.BoundingRectangle.Width > 0
                && element.Current.BoundingRectangle.Height > 0;
        },
        TimeSpan.FromSeconds(15),
        description);
    return element!;
}

static async Task WaitForExternalUriCountAsync(string path, int count, string description)
{
    await WaitUntilAsync(
        () => File.Exists(path) && File.ReadAllLines(path).Length >= count,
        TimeSpan.FromSeconds(10),
        description);
}

static async Task<AutomationElement> WaitForVisibleButtonAsync(
    AutomationElement root,
    string name,
    string description)
{
    var element = FindButtonByName(root, name)
        ?? throw new InvalidOperationException($"{description} was not found.");
    if (element.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var pattern))
    {
        ((ScrollItemPattern)pattern).ScrollIntoView();
    }

    AutomationElement? visibleElement = null;
    await WaitUntilAsync(
        () =>
        {
            visibleElement = FindButtonByName(root, name);
            if (visibleElement is null)
            {
                return false;
            }

            try
            {
                var bounds = visibleElement.Current.BoundingRectangle;
                return !visibleElement.Current.IsOffscreen && bounds.Width > 0 && bounds.Height > 0;
            }
            catch (ElementNotAvailableException)
            {
                return false;
            }
        },
        TimeSpan.FromSeconds(15),
        description);
    return visibleElement!;
}

static async Task StopTestAppAsync(Process process, string appPath)
{
    process.Refresh();
    if (process.HasExited)
    {
        return;
    }

    _ = process.CloseMainWindow();
    try
    {
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }
    catch (TimeoutException)
    {
        process.Refresh();
        if (!process.HasExited
            && string.Equals(process.MainModule?.FileName, appPath, StringComparison.OrdinalIgnoreCase))
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }
}

static string GetAppPath() => GetRepositoryPath(
    "src", "AIDrawer.App", "bin", "x64", "Debug",
    "net10.0-windows10.0.26100.0", "win-x64", "AIDrawer.App.exe");

static string GetRepositoryPath(params string[] segments)
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
         directory is not null;
         directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "global.json")) &&
            File.Exists(Path.Combine(directory.FullName, "src", "AIDrawer.App", "MainPage.xaml")))
        {
            return Path.Combine([directory.FullName, .. segments]);
        }
    }

    throw new DirectoryNotFoundException("Could not locate the AI Drawer repository root.");
}

static void AssertProviderChoicesCenteredAndVisible(AutomationElement root)
{
    var chooser = FindByName(root, "AI provider choices")
        ?? throw new InvalidOperationException("Provider chooser was not found.");
    var choices = chooser.FindAll(
        TreeScope.Descendants,
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
    Equal(ProviderCatalog.AvailableProviders.Count, choices.Count);

    var rootBounds = root.Current.BoundingRectangle;
    foreach (AutomationElement choice in choices)
    {
        var bounds = choice.Current.BoundingRectangle;
        True(bounds.Width > 0);
        True(bounds.Left >= rootBounds.Left - 1);
        True(bounds.Right <= rootBounds.Right + 1);
    }

    var geminiBounds = FindByName(root, "Open Gemini workspace")!.Current.BoundingRectangle;
    var chatGptBounds = FindByName(root, "Open ChatGPT workspace")!.Current.BoundingRectangle;
    var windowCenter = rootBounds.Left + (rootBounds.Width / 2);
    if (Math.Abs(geminiBounds.Top - chatGptBounds.Top) <= 2)
    {
        var groupCenter = (geminiBounds.Left + chatGptBounds.Right) / 2;
        True(Math.Abs(groupCenter - windowCenter) <= 2);
        return;
    }

    True(Math.Abs(geminiBounds.Left + (geminiBounds.Width / 2) - windowCenter) <= 2);
    True(Math.Abs(chatGptBounds.Left + (chatGptBounds.Width / 2) - windowCenter) <= 2);
}

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

    internal static void Resize(IntPtr windowHandle, int width, int height)
    {
        if (!GetWindowRect(windowHandle, out var bounds)
            || !MoveWindow(windowHandle, bounds.Left, bounds.Top, width, height, repaint: true))
        {
            throw new InvalidOperationException(
                $"Could not resize the application window (Windows error {Marshal.GetLastWin32Error()}).");
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

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rectangle);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveWindow(
        IntPtr windowHandle,
        int x,
        int y,
        int width,
        int height,
        [MarshalAs(UnmanagedType.Bool)] bool repaint);
}
