namespace AIDrawer.Core;

public enum WebViewFailureKind
{
    OutOfMemory,
    RendererUnresponsive,
    RendererExited,
    BrowserExited,
    FrameRendererExited,
    GpuOrUtilityExited,
    Other
}

public enum WebViewRecoveryAction
{
    ReleaseInactiveWorkspaces,
    WaitForRenderer,
    RequireManualRecovery,
    ReloadOnce,
    RestartWorkspace,
    RecreateBrowserEnvironment
}

public sealed record WebViewRecoveryDecision(
    WebViewRecoveryAction Action,
    bool RequiresRecovery);

public static class WebViewRecoveryPolicy
{
    public static WebViewRecoveryDecision Decide(
        WebViewFailureKind failureKind,
        int previousUnresponsiveFailures,
        int previousRendererReloadAttempts)
    {
        if (previousUnresponsiveFailures < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(previousUnresponsiveFailures));
        }

        if (previousRendererReloadAttempts < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(previousRendererReloadAttempts));
        }

        return failureKind switch
        {
            WebViewFailureKind.OutOfMemory => new(
                WebViewRecoveryAction.ReleaseInactiveWorkspaces,
                RequiresRecovery: true),
            WebViewFailureKind.RendererUnresponsive when previousUnresponsiveFailures == 0 => new(
                WebViewRecoveryAction.WaitForRenderer,
                RequiresRecovery: false),
            WebViewFailureKind.RendererUnresponsive => new(
                WebViewRecoveryAction.RequireManualRecovery,
                RequiresRecovery: true),
            WebViewFailureKind.RendererExited when previousRendererReloadAttempts == 0 => new(
                WebViewRecoveryAction.ReloadOnce,
                RequiresRecovery: false),
            WebViewFailureKind.RendererExited => new(
                WebViewRecoveryAction.RestartWorkspace,
                RequiresRecovery: false),
            WebViewFailureKind.BrowserExited => new(
                WebViewRecoveryAction.RecreateBrowserEnvironment,
                RequiresRecovery: false),
            _ => new(WebViewRecoveryAction.RequireManualRecovery, RequiresRecovery: true)
        };
    }
}
