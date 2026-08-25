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
            args.ResultFilePath = DownloadPolicy.CreateNonExistingPath(directory, assessment.SafeFileName, PathExists);
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

    private static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);
}

internal sealed record DownloadRequest(
    string WorkspaceId,
    string ProviderName,
    string SafeFileName,
    DownloadRisk Risk,
    string DestinationDirectory);

internal sealed record DownloadDecision(bool Allowed);
