using System.Globalization;

namespace AIDrawer.Core;

public sealed class RestoreLocatorPolicy(string providerHost, IReadOnlyList<string> allowedPathPrefixes)
{
    private const int MaximumPathLength = 2048;
    private readonly string _providerHost = new IdnMapping().GetAscii(providerHost).ToLowerInvariant();
    private readonly string[] _allowedPathPrefixes = allowedPathPrefixes
        .Where(prefix => prefix.StartsWith("/", StringComparison.Ordinal))
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    public Uri? Restrict(string? rawUri)
    {
        if (_allowedPathPrefixes.Length == 0
            || !Uri.TryCreate(rawUri, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !uri.IsDefaultPort
            || uri.AbsolutePath.Length > MaximumPathLength
            || !string.Equals(uri.IdnHost, _providerHost, StringComparison.OrdinalIgnoreCase)
            || !_allowedPathPrefixes.Any(prefix => uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return null;
        }

        return new UriBuilder(Uri.UriSchemeHttps, _providerHost)
        {
            Path = uri.AbsolutePath,
            Query = string.Empty,
            Fragment = string.Empty,
            Port = -1
        }.Uri;
    }
}
