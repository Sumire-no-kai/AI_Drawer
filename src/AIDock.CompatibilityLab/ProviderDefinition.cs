namespace AIDock.CompatibilityLab;

internal sealed record ProviderDefinition(
    string Id,
    string DisplayName,
    string CompatibilityStatus,
    Uri HomeUri,
    IReadOnlySet<string> KnownPurchaseHosts,
    IReadOnlyList<string> KnownPurchasePathFragments)
{
    public bool IsProviderAppUri(string? rawUri) =>
        TryCreateSafeHttpsUri(rawUri, out var uri)
        && string.Equals(uri.IdnHost, HomeUri.IdnHost, StringComparison.OrdinalIgnoreCase);

    public bool IsSafeObservationUri(string? rawUri) => TryCreateSafeHttpsUri(rawUri, out _);

    public bool IsKnownPurchaseUri(string? rawUri)
    {
        if (!TryCreateSafeHttpsUri(rawUri, out var uri))
        {
            return false;
        }

        if (KnownPurchaseHosts.Contains(uri.IdnHost))
        {
            return true;
        }

        return string.Equals(uri.IdnHost, HomeUri.IdnHost, StringComparison.OrdinalIgnoreCase)
            && KnownPurchasePathFragments.Any(fragment =>
                uri.AbsolutePath.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    public static string CreateOriginLabel(string? rawUri) =>
        TryCreateSafeHttpsUri(rawUri, out var uri)
            ? $"https://{uri.IdnHost}"
            : "blocked or unavailable";

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

    public string PurchasePolicySummary => KnownPurchaseHosts.Count == 0
        && KnownPurchasePathFragments.Count == 0
        ? "Purchase policy: no known routes configured; manual validation required."
        : "Purchase policy: known routes configured; manual validation still required.";
}
