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

Check("hard limit can release a grace-protected workspace", () =>
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

Check("hard limit does not release a workspace with a protected operation", () =>
{
    var now = DateTimeOffset.UtcNow;
    var policy = new WorkspaceLifecyclePolicy();
    var states = new[]
    {
        new LiveWorkspaceState("active", true, false, now, now),
        new LiveWorkspaceState("opening", false, false, now, now.AddMinutes(-2), true),
        new LiveWorkspaceState("ordinary", false, false, now, now.AddMinutes(-1))
    };
    Equal("ordinary", policy.SelectForDisposal(states, now, true).Single());
});

Check("hard limit returns no victim when every inactive workspace has a protected operation", () =>
{
    var now = DateTimeOffset.UtcNow;
    var policy = new WorkspaceLifecyclePolicy();
    var states = new[]
    {
        new LiveWorkspaceState("active", true, false, now, now),
        new LiveWorkspaceState("permission", false, false, now, now.AddMinutes(-2), true),
        new LiveWorkspaceState("download", false, false, now, now.AddMinutes(-1), true)
    };
    Equal(0, policy.SelectForDisposal(states, now, true).Count);
});

Check("negative grace period is rejected", () =>
{
    Throws<ArgumentOutOfRangeException>(() =>
        _ = new WorkspaceLifecyclePolicy(gracePeriod: TimeSpan.FromSeconds(-1)));
});

Check("support reminder never appears during the first seven days", () =>
{
    var now = DateTimeOffset.UtcNow;
    var policy = new SupportReminderPolicy(currentMajorRelease: 1);
    False(policy.IsEligible(now, now.AddDays(-6), 20, false, null, 0));
});

Check("support reminder becomes eligible after twenty opens and seven days", () =>
{
    var now = DateTimeOffset.UtcNow;
    var policy = new SupportReminderPolicy(currentMajorRelease: 1);
    True(policy.IsEligible(now, now.AddDays(-7), 20, false, null, 0));
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
    False(policy.IsEligible(now, null, 20, false, null, 0));
    False(policy.IsEligible(now, now.AddDays(1), 20, false, null, 0));
});

Check("support reminder permanent dismissal wins over eligibility", () =>
{
    var now = DateTimeOffset.UtcNow;
    var policy = new SupportReminderPolicy(currentMajorRelease: 1);
    False(policy.IsEligible(now, now.AddDays(-30), 20, true, null, 0));
});

Check("support reminder snooze requires both ninety days and a later major release", () =>
{
    var now = DateTimeOffset.UtcNow;
    var firstUsed = now.AddDays(-30);
    var policy = new SupportReminderPolicy(currentMajorRelease: 1);
    var snooze = policy.CreateSnooze(now);

    Equal(now.AddDays(90), snooze.UntilUtc);
    Equal(2, snooze.UntilMajorRelease);
    False(policy.IsEligible(now.AddDays(91), firstUsed, 20, false, snooze.UntilUtc, snooze.UntilMajorRelease));

    var nextMajorPolicy = new SupportReminderPolicy(currentMajorRelease: 2);
    False(nextMajorPolicy.IsEligible(now.AddDays(89), firstUsed, 20, false, snooze.UntilUtc, snooze.UntilMajorRelease));
    True(nextMajorPolicy.IsEligible(now.AddDays(91), firstUsed, 20, false, snooze.UntilUtc, snooze.UntilMajorRelease));
});

Check("legacy support snooze without a major release still honors its date", () =>
{
    var now = DateTimeOffset.UtcNow;
    var policy = new SupportReminderPolicy(currentMajorRelease: 1);
    False(policy.IsEligible(now, now.AddDays(-30), 20, false, now.AddDays(1), 0));
    True(policy.IsEligible(now.AddDays(2), now.AddDays(-30), 20, false, now.AddDays(1), 0));
});

Check("support reminder policy rejects an invalid major release", () =>
{
    Throws<ArgumentOutOfRangeException>(() => _ = new SupportReminderPolicy(0));
});

if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("17 core policy checks passed.");
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
