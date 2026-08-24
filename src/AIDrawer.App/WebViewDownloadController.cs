using AIDrawer.Core;
using Microsoft.Web.WebView2.Core;

namespace AIDrawer;

internal sealed class WebViewDownloadController(
    Func<DownloadRequest, Task<DownloadDecision>> requestDecisionAsync)
{
    internal async Task<bool> PrepareAsync(
        string workspaceId,
        string providerName,
        CoreWebView2DownloadStartingEventArgs args)
    {
        try
        {
            var directory = Path.GetDirectoryName(args.ResultFilePath);
            if (string.IsNullOrWhiteSpace(directory)
                || !Path.IsPathFullyQualified(directory)
                || !Directory.Exists(directory))
            {
                args.Cancel = true;
                return false;
            }

            var assessment = DownloadPolicy.Assess(Path.GetFileName(args.ResultFilePath));
            args.ResultFilePath = CreateNonExistingPath(directory, assessment.SafeFileName);
            args.Handled = false;
            var decision = await requestDecisionAsync(new DownloadRequest(
                workspaceId,
                providerName,
                assessment.SafeFileName,
                assessment.Risk,
                directory));
            args.Cancel = !decision.Allowed;
            return decision.Allowed;
        }
        catch
        {
            args.Cancel = true;
            return false;
        }
    }

    private static string CreateNonExistingPath(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        if (!PathExists(candidate))
        {
            return candidate;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var suffix = 2; suffix <= 9999; suffix++)
        {
            candidate = Path.Combine(directory, $"{stem} ({suffix}){extension}");
            if (!PathExists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, $"{stem}-{Guid.NewGuid():N}{extension}");
    }

    private static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);
}

internal sealed record DownloadRequest(
    string WorkspaceId,
    string ProviderName,
    string SafeFileName,
    DownloadRisk Risk,
    string DestinationDirectory);

internal sealed record DownloadDecision(bool Allowed);
