using System.Buffers;

namespace AIDrawer.Core;

public static class DownloadPolicy
{
    private const int MaximumFileNameLength = 180;
    private static readonly SearchValues<char> InvalidFileNameCharacters =
        SearchValues.Create("<>:\"/\\|?*");
    private static readonly HashSet<string> CommonExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".7z", ".avi", ".bmp", ".csv", ".doc", ".docx", ".epub", ".gif", ".gz",
        ".jpeg", ".jpg", ".json", ".md", ".mov", ".mp3", ".mp4", ".odp", ".ods",
        ".odt", ".pdf", ".png", ".ppt", ".pptx", ".rar", ".rtf", ".svg", ".tar",
        ".tif", ".tiff", ".txt", ".wav", ".webm", ".webp", ".xls", ".xlsx", ".xml", ".zip"
    };
    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".appx", ".appxbundle", ".bat", ".chm", ".cmd", ".com", ".cpl", ".dll", ".exe",
        ".hta", ".img", ".iso", ".jar", ".js", ".jse", ".lnk", ".msi", ".msix",
        ".msixbundle", ".ps1", ".ps1xml", ".psc1", ".psd1", ".psm1", ".reg", ".scr",
        ".url", ".vbe", ".vbs", ".wsf", ".wsh"
    };
    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static DownloadAssessment Assess(string? suggestedFileName)
    {
        var safeFileName = SanitizeFileName(suggestedFileName);
        var extension = Path.GetExtension(safeFileName);
        var risk = ExecutableExtensions.Contains(extension)
            ? DownloadRisk.Executable
            : CommonExtensions.Contains(extension)
                ? DownloadRisk.Common
                : DownloadRisk.Uncommon;
        return new DownloadAssessment(safeFileName, risk);
    }

    public static string SanitizeFileName(string? suggestedFileName)
    {
        var source = Path.GetFileName(suggestedFileName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(source))
        {
            return "download";
        }

        var characters = source
            .Select(character => IsUnsafeFileNameCharacter(character) ? '_' : character)
            .ToArray();
        var sanitized = new string(characters).Trim().TrimEnd('.', ' ');
        if (sanitized.Length == 0)
        {
            return "download";
        }

        if (sanitized.Length > MaximumFileNameLength)
        {
            var extension = Path.GetExtension(sanitized);
            if (extension.Length >= MaximumFileNameLength)
            {
                sanitized = sanitized[..MaximumFileNameLength].TrimEnd('.', ' ');
            }
            else
            {
                var stemLength = MaximumFileNameLength - extension.Length;
                sanitized = string.Concat(sanitized.AsSpan(0, stemLength), extension);
            }
        }

        var baseName = Path.GetFileNameWithoutExtension(sanitized);
        return ReservedWindowsNames.Contains(baseName) ? $"_{sanitized}" : sanitized;
    }

    private static bool IsUnsafeFileNameCharacter(char character) =>
        char.IsControl(character)
        || InvalidFileNameCharacters.Contains(character)
        || character is '\u061C' or '\u200E' or '\u200F'
        || character is >= '\u202A' and <= '\u202E'
        || character is >= '\u2066' and <= '\u206F';

    public static string CreateNonExistingPath(
        string directory,
        string fileName,
        Func<string, bool> pathExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(pathExists);

        var safeFileName = SanitizeFileName(fileName);
        var candidate = Path.Combine(directory, safeFileName);
        if (!pathExists(candidate))
        {
            return candidate;
        }

        var stem = Path.GetFileNameWithoutExtension(safeFileName);
        var extension = Path.GetExtension(safeFileName);
        for (var suffix = 2; suffix <= 9999; suffix++)
        {
            candidate = Path.Combine(directory, $"{stem} ({suffix}){extension}");
            if (!pathExists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, $"{stem}-{Guid.NewGuid():N}{extension}");
    }
}

public sealed record DownloadAssessment(string SafeFileName, DownloadRisk Risk);

public enum DownloadRisk
{
    Common,
    Uncommon,
    Executable
}
