using AIDrawer.Core;

var failures = new List<string>();

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

Check("grace keeps a bounded third live workspace", () =>
{
    var now = DateTimeOffset.UtcNow;
    var policy = new WorkspaceLifecyclePolicy(gracePeriod: TimeSpan.FromMinutes(5));
    var states = new[]
    {
        new LiveWorkspaceState("active", true, false, now, now),
        new LiveWorkspaceState("recent-1", false, false, now.AddMinutes(4), now.AddMinutes(-1)),
        new LiveWorkspaceState("recent-2", false, false, now.AddMinutes(4), now.AddMinutes(-2))
    };
    Equal(0, policy.SelectForDisposal(states, now, false).Count);
});

Check("expired least-recent non-kept workspace is disposed first", () =>
{
    var now = DateTimeOffset.UtcNow;
    var policy = new WorkspaceLifecyclePolicy();
    var states = new[]
    {
        new LiveWorkspaceState("active", true, false, now, now),
        new LiveWorkspaceState("kept", false, true, now.AddMinutes(-1), now.AddMinutes(-10)),
        new LiveWorkspaceState("ordinary", false, false, now.AddMinutes(-1), now.AddMinutes(-5))
    };
    Equal("ordinary", policy.SelectForDisposal(states, now, false).Single());
});

Check("hard limit can release a protected workspace", () =>
{
    var now = DateTimeOffset.UtcNow;
    var policy = new WorkspaceLifecyclePolicy();
    var states = new[]
    {
        new LiveWorkspaceState("active", true, false, now, now),
        new LiveWorkspaceState("old", false, false, now.AddMinutes(5), now.AddMinutes(-2)),
        new LiveWorkspaceState("new", false, false, now.AddMinutes(5), now.AddMinutes(-1))
    };
    Equal("old", policy.SelectForDisposal(states, now, true).Single());
});

if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("6 core policy checks passed.");
return 0;

void Check(string name, Action test)
{
    try
    {
        test();
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
