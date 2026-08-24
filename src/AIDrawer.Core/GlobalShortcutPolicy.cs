namespace AIDrawer.Core;

[Flags]
public enum GlobalShortcutModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8
}

public sealed record GlobalShortcutSettings(
    bool Enabled = true,
    GlobalShortcutModifiers Modifiers = GlobalShortcutModifiers.Windows | GlobalShortcutModifiers.Shift,
    string Key = "A")
{
    public static GlobalShortcutSettings Default { get; } = new();
}

public static class GlobalShortcutPolicy
{
    private const GlobalShortcutModifiers SupportedModifiers =
        GlobalShortcutModifiers.Alt
        | GlobalShortcutModifiers.Control
        | GlobalShortcutModifiers.Shift
        | GlobalShortcutModifiers.Windows;

    public static GlobalShortcutSettings Normalize(GlobalShortcutSettings? settings)
    {
        if (settings is null)
        {
            return GlobalShortcutSettings.Default;
        }

        var key = settings.Key.Trim().ToUpperInvariant();
        var modifiers = settings.Modifiers & SupportedModifiers;
        return IsValid(settings.Enabled, modifiers, key)
            ? settings with { Modifiers = modifiers, Key = key }
            : GlobalShortcutSettings.Default;
    }

    public static bool IsValid(GlobalShortcutSettings settings) =>
        IsValid(settings.Enabled, settings.Modifiers, settings.Key);

    public static string Format(GlobalShortcutSettings settings)
    {
        var normalized = Normalize(settings);
        if (!normalized.Enabled)
        {
            return "Disabled";
        }

        var parts = new List<string>(5);
        if (normalized.Modifiers.HasFlag(GlobalShortcutModifiers.Windows))
        {
            parts.Add("Win");
        }

        if (normalized.Modifiers.HasFlag(GlobalShortcutModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (normalized.Modifiers.HasFlag(GlobalShortcutModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (normalized.Modifiers.HasFlag(GlobalShortcutModifiers.Shift))
        {
            parts.Add("Shift");
        }

        parts.Add(normalized.Key);
        return string.Join(" + ", parts);
    }

    private static bool IsValid(
        bool enabled,
        GlobalShortcutModifiers modifiers,
        string key)
    {
        if (!enabled)
        {
            return true;
        }

        var hasPrimaryModifier = (modifiers
            & (GlobalShortcutModifiers.Windows | GlobalShortcutModifiers.Control | GlobalShortcutModifiers.Alt)) != 0;
        return hasPrimaryModifier
            && (modifiers & ~SupportedModifiers) == 0
            && key.Length == 1
            && key[0] is >= 'A' and <= 'Z';
    }
}
