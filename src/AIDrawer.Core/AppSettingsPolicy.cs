namespace AIDrawer.Core;

public static class AppSettingsPolicy
{
    public static AppSettings Normalize(AppSettings settings)
    {
        var providerId = settings.DefaultProviderId?.Trim();
        if (providerId is { Length: > 64 }
            || providerId?.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_') == true)
        {
            providerId = null;
        }

        var placement = settings.WindowPlacement;
        if (placement is not null && !WindowPlacementPolicy.IsValid(placement))
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
