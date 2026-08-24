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

    internal bool IsAuthenticationUri(string? rawUri) =>
        TryCreateSafeHttpsUri(rawUri, out var uri) && IsAllowedHost(uri.IdnHost, AuthenticationDomains);

    internal bool IsKnownPurchaseUri(string? rawUri)
    {
        return TryCreateSafeHttpsUri(rawUri, out var uri) && IsKnownPurchaseUri(uri);
    }

    internal NavigationDisposition ClassifyTopLevelNavigation(string? rawUri)
    {
        if (!TryCreateSafeHttpsUri(rawUri, out var uri))
        {
            return NavigationDisposition.BlockUnsupported;
        }

        if (IsKnownPurchaseUri(uri))
        {
            return NavigationDisposition.BlockPurchase;
        }

        if (IsAllowedHost(uri.IdnHost, AppDomains))
        {
            return NavigationDisposition.EmbedProviderApplication;
        }

        if (IsAllowedHost(uri.IdnHost, AuthenticationDomains))
        {
            return NavigationDisposition.EmbedAuthentication;
        }

        return NavigationDisposition.OpenExternal;
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
        return ClassifyTopLevelNavigation(rawUri) switch
        {
            NavigationDisposition.EmbedProviderApplication => PopupDisposition.OpenControlledProviderWindow,
            NavigationDisposition.EmbedAuthentication => PopupDisposition.OpenControlledAuthenticationWindow,
            NavigationDisposition.OpenExternal => PopupDisposition.OpenExternal,
            NavigationDisposition.BlockPurchase => PopupDisposition.BlockPurchase,
            _ => PopupDisposition.BlockUnsupported
        };
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

    private bool IsKnownPurchaseUri(Uri uri)
    {
        if (IsAllowedHost(uri.IdnHost, KnownPurchaseHosts))
        {
            return true;
        }

        return IsAllowedHost(uri.IdnHost, AppDomains)
            && KnownPurchasePathFragments.Any(fragment =>
                uri.AbsolutePath.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAllowedHost(string host, IReadOnlySet<string> domains) =>
        domains.Contains(host);
}

internal enum NavigationDisposition
{
    EmbedProviderApplication,
    EmbedAuthentication,
    OpenExternal,
    BlockPurchase,
    BlockUnsupported
}

internal enum PopupDisposition
{
    OpenControlledProviderWindow,
    OpenControlledAuthenticationWindow,
    OpenExternal,
    BlockPurchase,
    BlockUnsupported
}
