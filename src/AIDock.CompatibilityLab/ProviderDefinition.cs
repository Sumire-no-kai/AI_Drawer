namespace AIDock.CompatibilityLab;

internal sealed record ProviderDefinition(
    string Id,
    string DisplayName,
    string CompatibilityStatus,
    Uri HomeUri,
    IReadOnlySet<string> KnownPurchaseHosts,
    IReadOnlyList<string> KnownPurchasePathFragments)
{
    public bool IsKnownPurchaseUri(string? rawUri)
    {
        if (!Uri.TryCreate(rawUri, UriKind.Absolute, out var uri))
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

    public string PurchasePolicySummary => KnownPurchaseHosts.Count == 0
        && KnownPurchasePathFragments.Count == 0
        ? "Purchase policy: no known routes configured; manual validation required."
        : "Purchase policy: known routes configured; manual validation still required.";
}
