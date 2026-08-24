namespace AIDrawer.Core;

public static class AppSettingsPolicy
{
    private const int MaximumWindowDimension = short.MaxValue;
    private const int MinimumWindowWidth = 720;
    private const int MinimumWindowHeight = 540;

    public static AppSettings Normalize(AppSettings settings)
    {
        var providerId = settings.DefaultProviderId?.Trim();
        if (providerId is { Length: > 64 }
            || providerId?.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_') == true)
        {
            providerId = null;
        }

        var placement = settings.WindowPlacement;
        if (placement is not null
            && (placement.Width < MinimumWindowWidth
                || placement.Height < MinimumWindowHeight
                || placement.Width > MaximumWindowDimension
                || placement.Height > MaximumWindowDimension))
        {
            placement = null;
        }

        return settings with
        {
            MemoryMode = MemoryMode.Balanced,
            SuccessfulOpenCount = Math.Max(0, settings.SuccessfulOpenCount),
            SupportReminderSnoozedUntilMajorRelease = Math.Max(0, settings.SupportReminderSnoozedUntilMajorRelease),
            DefaultProviderId = string.IsNullOrEmpty(providerId) ? null : providerId,
            GlobalShortcut = GlobalShortcutPolicy.Normalize(settings.GlobalShortcut),
            WindowPlacement = placement
        };
    }
}
