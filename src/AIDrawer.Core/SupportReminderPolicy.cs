namespace AIDrawer.Core;

public sealed class SupportReminderPolicy
{
    private static readonly TimeSpan MinimumAge = TimeSpan.FromDays(7);
    private static readonly TimeSpan TimeEligibilityAge = TimeSpan.FromDays(14);
    private static readonly TimeSpan SnoozeDuration = TimeSpan.FromDays(90);

    public const int SuccessfulOpenThreshold = 20;

    private readonly int _currentMajorRelease;

    public SupportReminderPolicy(int currentMajorRelease)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(currentMajorRelease, 1);
        _currentMajorRelease = currentMajorRelease;
    }

    public bool IsEligible(
        DateTimeOffset nowUtc,
        DateTimeOffset? firstUsedUtc,
        int successfulOpenCount,
        bool permanentlyDismissed,
        DateTimeOffset? snoozedUntilUtc,
        int snoozedUntilMajorRelease)
    {
        if (permanentlyDismissed
            || firstUsedUtc is not { } firstUsed
            || firstUsed > nowUtc
            || snoozedUntilUtc > nowUtc
            || snoozedUntilMajorRelease > _currentMajorRelease)
        {
            return false;
        }

        var age = nowUtc - firstUsed;
        return age >= MinimumAge
            && (age >= TimeEligibilityAge || successfulOpenCount >= SuccessfulOpenThreshold);
    }

    public SupportReminderSnooze CreateSnooze(DateTimeOffset nowUtc) =>
        new(nowUtc.Add(SnoozeDuration), checked(_currentMajorRelease + 1));
}

public sealed record SupportReminderSnooze(
    DateTimeOffset UntilUtc,
    int UntilMajorRelease);
