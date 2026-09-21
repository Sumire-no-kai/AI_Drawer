using AIDrawer.Core;

var failures = new List<string>();
var completedCheckCount = 0;

Check("locator strips query and fragment", () =>
{
    var policy = new RestoreLocatorPolicy("chatgpt.com", ["/c/"]);
    var result = policy.Restrict("https://chatgpt.com/c/opaque-id?token=secret#message");
    Equal("https://chatgpt.com/c/opaque-id", result?.AbsoluteUri.TrimEnd('/'));
});

Check("locator rejects auth, subdomain, port, and unknown path", () =>
{
    var policy = new RestoreLocatorPolicy("chatgpt.com", ["/c/"]);
    Null(policy.Restrict("https://auth.openai.com/c/id"));
    Null(policy.Restrict("https://evil.chatgpt.com/c/id"));
    Null(policy.Restrict("https://chatgpt.com:8443/c/id"));
    Null(policy.Restrict("https://chatgpt.com/share/id"));
});

Check("no reviewed path means no locator", () =>
{
    var policy = new RestoreLocatorPolicy("example.com", []);
    Null(policy.Restrict("https://example.com/c/id"));
});

Check("split selection replaces only the focused pane", () =>
{
    var layout = new WorkspaceLayout("a", "b", "b");
    Equal(new WorkspaceLayout("a", "c", "c"), layout.Select("c"));
    Equal(new WorkspaceLayout("a", "b", "a"), layout.Select("a"));
});

Check("pairing never places the same workspace in both panes", () =>
{
    var layout = new WorkspaceLayout("a", null, "a");
    Equal(layout, layout.PairWith("a"));
    Equal(new WorkspaceLayout("a", "b", "b"), layout.PairWith("b"));
});

Check("ending split retains the focused page", () =>
{
    Equal(new WorkspaceLayout("b", null, "b"), new WorkspaceLayout("a", "b", "b").EndSplit());
});

Check("narrow layout keeps the pair but displays only focus", () =>
{
    var layout = new WorkspaceLayout("a", "b", "b");
    Equal("b", string.Join(',', layout.VisibleWorkspaceIds(false)));
    Equal("a,b", string.Join(',', layout.VisibleWorkspaceIds(true)));
});

Check("removing a pane normalizes focus and invalid references", () =>
{
    Equal(new WorkspaceLayout("b", null, "b"), new WorkspaceLayout("a", "b", "a").Normalize(["b"], "b"));
    Equal(new WorkspaceLayout("a", null, "a"), new WorkspaceLayout("a", "b", "b").Normalize(["a"]));
    Equal(new WorkspaceLayout(), new WorkspaceLayout("a", "b", "a").Normalize([]));
});

Check("layout rejects duplicate panes and invalid ratios", () =>
{
    Equal(new WorkspaceLayout("a", null, "a"), new WorkspaceLayout("a", "a", "missing", double.NaN).Normalize(["a"]));
    Equal(0.75, new WorkspaceLayout("a", "b", "a", 100).Normalize(["a", "b"]).PrimaryPaneRatio);
});

Check("reference links preserve explicit search parameters and code anchors", () =>
{
    Equal("https://example.com/search?q=terms&page=2#section-1",
        ExternalReferencePolicy.CreateTarget("https://example.com/search?q=terms&page=2&utm_source=ignored#section-1", true)?.AbsoluteUri);
    Equal("https://example.com/file#L120-L130", ExternalReferencePolicy.CreateTarget("https://example.com/file#L120-L130", true)?.AbsoluteUri);
});

Check("reference links do not forward credentials or automatic navigation parameters", () =>
{
    Equal("https://example.com/path", ExternalReferencePolicy.CreateTarget("https://example.com/path?q=x&code=secret#section", true)?.AbsoluteUri);
    Equal("https://example.com/path", ExternalReferencePolicy.CreateTarget("https://example.com/path?q=x#section", false)?.AbsoluteUri);
    Equal("https://example.com/path", ExternalReferencePolicy.CreateTarget("https://example.com/path#access_token=secret", true)?.AbsoluteUri);
    Null(ExternalReferencePolicy.CreateTarget("http://example.com/path", true));
    Null(ExternalReferencePolicy.CreateTarget("https://user@example.com/path", true));
});

Check("support reminder never appears during the first seven days", () =>
{
    var now = DateTimeOffset.UtcNow;
    var policy = new SupportReminderPolicy(currentMajorRelease: 1);
    False(policy.IsEligible(now, now.AddDays(-6), 7, false, null, 0));
});

Check("support reminder becomes eligible after seven opens and seven days", () =>
{
    var now = DateTimeOffset.UtcNow;
    var policy = new SupportReminderPolicy(currentMajorRelease: 1);
    False(policy.IsEligible(now, now.AddDays(-7), 6, false, null, 0));
    True(policy.IsEligible(now, now.AddDays(-7), 7, false, null, 0));
});

Check("support reminder becomes eligible after fourteen days", () =>
{
    var now = DateTimeOffset.UtcNow;
    var policy = new SupportReminderPolicy(currentMajorRelease: 1);
    True(policy.IsEligible(now, now.AddDays(-14), 0, false, null, 0));
});

Check("support reminder rejects missing or future first-use dates", () =>
{
    var now = DateTimeOffset.UtcNow;
    var policy = new SupportReminderPolicy(currentMajorRelease: 1);
    False(policy.IsEligible(now, null, 7, false, null, 0));
    False(policy.IsEligible(now, now.AddDays(1), 7, false, null, 0));
});

Check("support reminder permanent dismissal wins over eligibility", () =>
{
    var now = DateTimeOffset.UtcNow;
    var policy = new SupportReminderPolicy(currentMajorRelease: 1);
    False(policy.IsEligible(now, now.AddDays(-30), 7, true, null, 0));
});

Check("support reminder snooze requires both ninety days and a later major release", () =>
{
    var now = DateTimeOffset.UtcNow;
    var firstUsed = now.AddDays(-30);
    var policy = new SupportReminderPolicy(currentMajorRelease: 1);
    var snooze = policy.CreateSnooze(now);

    Equal(now.AddDays(90), snooze.UntilUtc);
    Equal(2, snooze.UntilMajorRelease);
    False(policy.IsEligible(now.AddDays(91), firstUsed, 7, false, snooze.UntilUtc, snooze.UntilMajorRelease));

    var nextMajorPolicy = new SupportReminderPolicy(currentMajorRelease: 2);
    False(nextMajorPolicy.IsEligible(now.AddDays(89), firstUsed, 7, false, snooze.UntilUtc, snooze.UntilMajorRelease));
    True(nextMajorPolicy.IsEligible(now.AddDays(91), firstUsed, 7, false, snooze.UntilUtc, snooze.UntilMajorRelease));
});

Check("legacy support snooze without a major release still honors its date", () =>
{
    var now = DateTimeOffset.UtcNow;
    var policy = new SupportReminderPolicy(currentMajorRelease: 1);
    False(policy.IsEligible(now, now.AddDays(-30), 7, false, now.AddDays(1), 0));
    True(policy.IsEligible(now.AddDays(2), now.AddDays(-30), 7, false, now.AddDays(1), 0));
});

Check("support reminder policy rejects an invalid major release", () =>
{
    Throws<ArgumentOutOfRangeException>(() => _ = new SupportReminderPolicy(0));
});

Check("global shortcut defaults to Win Shift A", () =>
{
    var shortcut = GlobalShortcutPolicy.Normalize(null);
    Equal(true, shortcut.Enabled);
    Equal(GlobalShortcutModifiers.Windows | GlobalShortcutModifiers.Shift, shortcut.Modifiers);
    Equal("A", shortcut.Key);
    Equal("Win + Shift + A", GlobalShortcutPolicy.Format(shortcut));
});

Check("global shortcut normalizes a supported key", () =>
{
    var shortcut = GlobalShortcutPolicy.Normalize(new GlobalShortcutSettings(
        true,
        GlobalShortcutModifiers.Control | GlobalShortcutModifiers.Alt,
        " q "));
    Equal("Q", shortcut.Key);
    Equal("Ctrl + Alt + Q", GlobalShortcutPolicy.Format(shortcut));
});

Check("global shortcut allows a disabled configuration", () =>
{
    var shortcut = GlobalShortcutPolicy.Normalize(new GlobalShortcutSettings(false, GlobalShortcutModifiers.None, string.Empty));
    Equal(false, shortcut.Enabled);
    Equal("Disabled", GlobalShortcutPolicy.Format(shortcut));
});

Check("global shortcut rejects an unsafe unmodified key", () =>
{
    Equal(false, GlobalShortcutPolicy.IsValid(new GlobalShortcutSettings(true, GlobalShortcutModifiers.Shift, "A")));
    Equal(GlobalShortcutSettings.Default, GlobalShortcutPolicy.Normalize(
        new GlobalShortcutSettings(true, GlobalShortcutModifiers.Shift, "A")));
});

Check("global shortcut rejects unsupported keys", () =>
{
    Equal(false, GlobalShortcutPolicy.IsValid(new GlobalShortcutSettings(true, GlobalShortcutModifiers.Windows, "F1")));
    Equal(false, GlobalShortcutPolicy.IsValid(new GlobalShortcutSettings(true, GlobalShortcutModifiers.Windows, "1")));
});

Check("download policy classifies common documents", () =>
{
    var assessment = DownloadPolicy.Assess("report.PDF");
    Equal("report.PDF", assessment.SafeFileName);
    Equal(DownloadRisk.Common, assessment.Risk);
});

Check("download policy classifies executable content", () =>
{
    var assessment = DownloadPolicy.Assess("installer.msixbundle");
    Equal(DownloadRisk.Executable, assessment.Risk);
});

Check("download policy classifies unknown content", () =>
{
    Equal(DownloadRisk.Uncommon, DownloadPolicy.Assess("archive.unknown").Risk);
    Equal(DownloadRisk.Uncommon, DownloadPolicy.Assess("no-extension").Risk);
});

Check("download policy removes path traversal and invalid characters", () =>
{
    Equal("unsafe_name_.txt", DownloadPolicy.SanitizeFileName("..\\unsafe:name?.txt"));
    Equal("download", DownloadPolicy.SanitizeFileName("..."));
});

Check("download policy removes control and bidirectional formatting characters", () =>
{
    Equal("invoice_cod.exe", DownloadPolicy.SanitizeFileName("invoice\u202Ecod.exe"));
    Equal("report_pdf_.txt", DownloadPolicy.SanitizeFileName("report\u2066pdf\u2069.txt"));
    Equal("line_break.txt", DownloadPolicy.SanitizeFileName("line\u0085break.txt"));
});

Check("download policy protects Windows device names", () =>
{
    Equal("_CON.txt", DownloadPolicy.SanitizeFileName("CON.txt"));
    Equal("_lpt9", DownloadPolicy.SanitizeFileName("lpt9"));
});

Check("download policy bounds long names while preserving an extension", () =>
{
    var safeName = DownloadPolicy.SanitizeFileName(new string('a', 250) + ".pdf");
    Equal(180, safeName.Length);
    Equal(".pdf", Path.GetExtension(safeName));
});

Check("download policy bounds a pathological long extension", () =>
{
    var safeName = DownloadPolicy.SanitizeFileName("a." + new string('x', 250));
    Equal(true, safeName.Length <= 180);
});

Check("download policy supplies a fallback name", () =>
{
    Equal("download", DownloadPolicy.SanitizeFileName(null));
    Equal("download", DownloadPolicy.SanitizeFileName(string.Empty));
});

Check("download policy chooses a non-overwriting sibling path", () =>
{
    var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Path.Combine("C:\\Downloads", "report.pdf"),
        Path.Combine("C:\\Downloads", "report (2).pdf")
    };
    Equal(
        Path.Combine("C:\\Downloads", "report (3).pdf"),
        DownloadPolicy.CreateNonExistingPath("C:\\Downloads", "report.pdf", existing.Contains));
});

Check("download policy sanitizes a proposed path before collision handling", () =>
{
    Equal(
        Path.Combine("C:\\Downloads", "_CON.txt"),
        DownloadPolicy.CreateNonExistingPath("C:\\Downloads", "CON.txt", _ => false));
});

Check("download policy treats an existing directory name as a collision", () =>
{
    var root = Path.Combine(Path.GetTempPath(), $"AI-Drawer-DownloadPolicy-{Guid.NewGuid():N}");
    Directory.CreateDirectory(Path.Combine(root, "report.pdf"));
    try
    {
        Equal(
            Path.Combine(root, "report (2).pdf"),
            DownloadPolicy.CreateNonExistingPath(
                root,
                "report.pdf",
                path => File.Exists(path) || Directory.Exists(path)));
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
});

Check("window placement policy clamps an off-screen oversized placement", () =>
{
    var result = WindowPlacementPolicy.ClampToWorkArea(
        new WindowPlacementSnapshot(5000, -1000, 2000, 1200),
        new WindowWorkArea(0, 0, 1280, 720));
    Equal(new WindowPlacementSnapshot(0, 0, 1280, 720), result);
});

Check("window placement policy preserves a valid placement in a negative-coordinate display", () =>
{
    var result = WindowPlacementPolicy.ClampToWorkArea(
        new WindowPlacementSnapshot(-1800, 100, 1100, 720),
        new WindowWorkArea(-1920, 0, 1920, 1080));
    Equal(new WindowPlacementSnapshot(-1800, 100, 1100, 720), result);
});

Check("window placement policy rejects invalid input and work areas", () =>
{
    Null(WindowPlacementPolicy.ClampToWorkArea(
        new WindowPlacementSnapshot(0, 0, 100, 100),
        new WindowWorkArea(0, 0, 1280, 720)));
    Null(WindowPlacementPolicy.ClampToWorkArea(
        new WindowPlacementSnapshot(0, 0, 720, 540),
        new WindowWorkArea(0, 0, 0, 720)));
});

Check("WebView security defaults disable all native-to-page bridges and browser conveniences", () =>
{
    var settings = WebViewSecurityPolicy.EmbeddedProviderDefaults;
    False(settings.AreDevToolsEnabled);
    False(settings.AreHostObjectsAllowed);
    False(settings.IsWebMessageEnabled);
    False(settings.IsPasswordAutosaveEnabled);
    False(settings.IsGeneralAutofillEnabled);
});

Check("WebView recovery policy performs one bounded renderer reload", () =>
{
    Equal(
        new WebViewRecoveryDecision(WebViewRecoveryAction.ReloadOnce, false),
        WebViewRecoveryPolicy.Decide(WebViewFailureKind.RendererExited, 0, 0));
    Equal(
        new WebViewRecoveryDecision(WebViewRecoveryAction.RestartWorkspace, false),
        WebViewRecoveryPolicy.Decide(WebViewFailureKind.RendererExited, 0, 1));
});

Check("WebView recovery policy waits once and then asks for explicit renderer recovery", () =>
{
    Equal(
        new WebViewRecoveryDecision(WebViewRecoveryAction.WaitForRenderer, false),
        WebViewRecoveryPolicy.Decide(WebViewFailureKind.RendererUnresponsive, 0, 0));
    Equal(
        new WebViewRecoveryDecision(WebViewRecoveryAction.RequireManualRecovery, true),
        WebViewRecoveryPolicy.Decide(WebViewFailureKind.RendererUnresponsive, 1, 0));
});

Check("WebView recovery policy preserves browser and memory boundaries", () =>
{
    Equal(
        new WebViewRecoveryDecision(WebViewRecoveryAction.RecreateBrowserEnvironment, false),
        WebViewRecoveryPolicy.Decide(WebViewFailureKind.BrowserExited, 0, 0));
    Equal(
        new WebViewRecoveryDecision(WebViewRecoveryAction.RequireManualRecovery, true),
        WebViewRecoveryPolicy.Decide(WebViewFailureKind.OutOfMemory, 0, 0));
    Equal(
        new WebViewRecoveryDecision(WebViewRecoveryAction.WaitForRenderer, false),
        WebViewRecoveryPolicy.Decide(WebViewFailureKind.FrameRendererExited, 0, 0));
    Equal(
        new WebViewRecoveryDecision(WebViewRecoveryAction.WaitForRenderer, false),
        WebViewRecoveryPolicy.Decide(WebViewFailureKind.GpuOrUtilityExited, 0, 0));
});

Check("WebView recovery policy rejects negative counters", () =>
{
    Throws<ArgumentOutOfRangeException>(() =>
        WebViewRecoveryPolicy.Decide(WebViewFailureKind.Other, -1, 0));
    Throws<ArgumentOutOfRangeException>(() =>
        WebViewRecoveryPolicy.Decide(WebViewFailureKind.Other, 0, -1));
});

Check("app settings normalize new optional fields", () =>
{
    var normalized = AppSettingsPolicy.Normalize(new AppSettings(
        SuccessfulOpenCount: -3,
        SupportReminderSnoozedUntilMajorRelease: -1,
        DefaultProviderId: " chatgpt ",
        GlobalShortcut: new GlobalShortcutSettings(true, GlobalShortcutModifiers.Control | GlobalShortcutModifiers.Alt, "q")));
    Equal(0, normalized.SuccessfulOpenCount);
    Equal(0, normalized.SupportReminderSnoozedUntilMajorRelease);
    Equal("chatgpt", normalized.DefaultProviderId);
    Equal("Q", normalized.GlobalShortcut?.Key);
});

Check("app settings reject an unsafe provider id", () =>
{
    var normalized = AppSettingsPolicy.Normalize(new AppSettings(DefaultProviderId: "../../profile"));
    Null(normalized.DefaultProviderId);
});

Check("app settings reject an invalid window placement", () =>
{
    var normalized = AppSettingsPolicy.Normalize(new AppSettings(
        WindowPlacement: new WindowPlacementSnapshot(0, 0, 100, 100)));
    Null(normalized.WindowPlacement);
});

if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine($"{completedCheckCount} core policy checks passed.");
return 0;

void Check(string name, Action test)
{
    try
    {
        test();
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

static void Null(object? actual)
{
    if (actual is not null)
    {
        throw new InvalidOperationException($"expected null, got {actual}");
    }
}

static void True(bool actual)
{
    if (!actual)
    {
        throw new InvalidOperationException("expected true, got false");
    }
}

static void False(bool actual)
{
    if (actual)
    {
        throw new InvalidOperationException("expected false, got true");
    }
}

static void Throws<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"expected {typeof(TException).Name}");
}
