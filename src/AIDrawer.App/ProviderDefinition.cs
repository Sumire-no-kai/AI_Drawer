namespace AIDrawer;

internal sealed record ProviderDefinition(
    string Id,
    string DisplayName,
    string CompatibilityStatus,
    Uri HomeUri,
    IReadOnlySet<string> AppDomains,
    IReadOnlySet<string> AuthenticationDomains,
    IReadOnlySet<string> KnownPurchaseHosts,
    IReadOnlyList<string> KnownPurchasePathFragments)
{
    internal string ProfileName => $"provider-{Id}";

    internal bool IsAllowedEmbeddedUri(string? rawUri)
    {
        if (!Uri.TryCreate(rawUri, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
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
        if (!Uri.TryCreate(rawUri, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
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

    private static bool IsAllowedHost(string host, IReadOnlySet<string> domains) => domains.Any(domain =>
        string.Equals(host, domain, StringComparison.OrdinalIgnoreCase)
        || host.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase));
}
