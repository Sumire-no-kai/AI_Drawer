using Windows.System;

namespace AIDrawer;

internal static class ExternalUriLauncher
{
    private const string AcceptanceLogFileName = "external-uri-launches.acceptance";

    internal static async Task<bool> LaunchAsync(Uri uri)
    {
#if DEBUG
        var recordForAcceptance = IsAcceptanceRecordingEnabled();
        var launchDuringAcceptance = string.Equals(
            Environment.GetEnvironmentVariable("AI_DRAWER_TEST_LAUNCH_EXTERNAL_URI"),
            "1",
            StringComparison.Ordinal);
        if (recordForAcceptance && !launchDuringAcceptance && TryRecordFixedAcceptanceUri(uri))
        {
            return true;
        }
#endif

        var launched = await Launcher.LaunchUriAsync(uri);
#if DEBUG
        if (launched && recordForAcceptance && launchDuringAcceptance)
        {
            _ = TryRecordFixedAcceptanceUri(uri);
        }
#endif
        return launched;
    }

#if DEBUG
    private static bool IsAcceptanceRecordingEnabled() => string.Equals(
        Environment.GetEnvironmentVariable("AI_DRAWER_TEST_RECORD_EXTERNAL_URI"),
        "1",
        StringComparison.Ordinal);

    private static bool TryRecordFixedAcceptanceUri(Uri uri)
    {
        var absoluteUri = uri.AbsoluteUri;
        if (absoluteUri is not (
            "https://forms.cloud.microsoft/r/WLQySVad7g"
            or "https://buymeacoffee.com/edward_lee"
            or "https://github.com/Sumire-no-kai/AI_Drawer/issues/new?template=provider_compatibility.yml"
            or "https://github.com/Sumire-no-kai/AI_Drawer/security/advisories/new"))
        {
            return false;
        }

        var testRoot = Environment.GetEnvironmentVariable("AI_DRAWER_TEST_DATA_ROOT");
        if (!TryResolveIsolatedTestRoot(testRoot, out var resolvedRoot))
        {
            return false;
        }

        try
        {
            File.AppendAllText(
                Path.Combine(resolvedRoot, AcceptanceLogFileName),
                absoluteUri + Environment.NewLine);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryResolveIsolatedTestRoot(string? testRoot, out string resolvedRoot)
    {
        resolvedRoot = string.Empty;
        if (string.IsNullOrWhiteSpace(testRoot) || !Path.IsPathFullyQualified(testRoot))
        {
            return false;
        }

        try
        {
            var fullRoot = Path.GetFullPath(testRoot).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var leafName = Path.GetFileName(fullRoot);
            if (!fullRoot.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)
                || !Directory.Exists(fullRoot)
                || (File.GetAttributes(fullRoot) & FileAttributes.ReparsePoint) != 0
                || !(leafName.StartsWith("AI-Drawer-SessionTests-", StringComparison.Ordinal)
                    || leafName.StartsWith("AI-Drawer-RuntimeAcceptance-", StringComparison.Ordinal)))
            {
                return false;
            }

            resolvedRoot = fullRoot;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
#endif
}
