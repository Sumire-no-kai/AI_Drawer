using AIDrawer.Core;

namespace AIDrawer;

internal sealed record ProviderDefinition(
    string Id,
    string DisplayName,
    string CompatibilityStatus,
    string? IconAssetUri,
    string IconFallback,
    Uri HomeUri,
    IReadOnlySet<string> AppDomains,
    IReadOnlySet<string> AuthenticationDomains,
    IReadOnlySet<string> KnownPurchaseHosts,
    IReadOnlyList<string> KnownPurchasePathFragments,
    IReadOnlyList<string> RestorePathPrefixes)
{
    internal string ProfileName => $"provider-{Id}";

    internal bool UsesMonochromeMark => Id is "chatgpt" or "grok";

    internal string WorkspaceLabel => Id switch
    {
        "doubao" => "Doubao",
        "qwen" => "Qwen",
        "glm" => "GLM",
        _ => DisplayName
    };

    internal Uri? CreateRestoreLocator(string? rawUri) =>
        new RestoreLocatorPolicy(HomeUri.IdnHost, RestorePathPrefixes).Restrict(rawUri);

    internal bool IsProviderAppUri(string? rawUri) =>
        TryCreateSafeHttpsUri(rawUri, out var uri) && IsAllowedHost(uri.IdnHost, AppDomains);

    internal bool IsAllowedEmbeddedUri(string? rawUri)
    {
        if (!TryCreateSafeHttpsUri(rawUri, out var uri))
        {
            return false;
        }

        return IsAllowedHost(uri.IdnHost, AppDomains)
            || IsAllowedHost(uri.IdnHost, AuthenticationDomains);
    }

    internal bool IsKnownPurchaseUri(string? rawUri)
    {
        if (!Uri.TryCreate(rawUri, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (IsAllowedHost(uri.IdnHost, KnownPurchaseHosts))
        {
            return true;
        }

        return IsAllowedHost(uri.IdnHost, AppDomains)
            && KnownPurchasePathFragments.Any(fragment =>
                uri.AbsolutePath.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    internal Uri? CreateSafeExternalUri(string? rawUri)
    {
        if (!TryCreateSafeHttpsUri(rawUri, out var uri))
        {
            return null;
        }

        var builder = new UriBuilder(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri;
    }

    internal PopupDisposition ClassifyPopup(string? rawUri)
    {
        if (IsKnownPurchaseUri(rawUri))
        {
            return PopupDisposition.BlockPurchase;
        }

        if (IsAllowedEmbeddedUri(rawUri))
        {
            return PopupDisposition.OpenControlledWindow;
        }

        return CreateSafeExternalUri(rawUri) is null
            ? PopupDisposition.BlockUnsupported
            : PopupDisposition.OpenExternal;
    }

    internal Uri? CreateSafeInMemoryUri(string? rawUri)
    {
        if (!TryCreateSafeHttpsUri(rawUri, out var uri)
            || !IsAllowedHost(uri.IdnHost, AppDomains)
            || uri.AbsolutePath.Length > 2048)
        {
            return null;
        }

        return new UriBuilder(Uri.UriSchemeHttps, uri.IdnHost)
        {
            Path = uri.AbsolutePath,
            Query = string.Empty,
            Fragment = string.Empty,
            Port = -1
        }.Uri;
    }

    private static bool TryCreateSafeHttpsUri(string? rawUri, out Uri uri)
    {
        if (Uri.TryCreate(rawUri, UriKind.Absolute, out var candidate)
            && string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(candidate.UserInfo)
            && candidate.IsDefaultPort)
        {
            uri = candidate;
            return true;
        }

        uri = null!;
        return false;
    }

    private static bool IsAllowedHost(string host, IReadOnlySet<string> domains) => domains.Any(domain =>
        string.Equals(host, domain, StringComparison.OrdinalIgnoreCase)
        || host.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase));
}

internal enum PopupDisposition
{
    OpenControlledWindow,
    OpenExternal,
    BlockPurchase,
    BlockUnsupported
}
