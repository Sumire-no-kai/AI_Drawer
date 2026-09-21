namespace AIDrawer.Core;

public static class ExternalReferencePolicy
{
    private static readonly HashSet<string> ReferenceParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "q", "query", "search", "id", "doc", "document", "article", "page", "p", "v", "version", "lang", "hl"
    };

    // A mixed authentication/reference URL must not partially forward a sign-in transaction.
    private static readonly HashSet<string> SensitiveParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "code", "state", "token", "access_token", "id_token", "refresh_token", "session", "session_id",
        "sessionid", "checkout", "checkout_session_id", "signature", "sig", "key", "api_key", "client_secret", "password"
    };

    public static Uri? CreateTarget(string? rawUri, bool userInitiated)
    {
        if (!Uri.TryCreate(rawUri, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        var target = new UriBuilder(uri) { Query = string.Empty, Fragment = string.Empty };
        if (!userInitiated)
        {
            return target.Uri;
        }

        var parameters = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        var names = parameters.Select(parameter => Uri.UnescapeDataString(parameter.Split('=', 2)[0])).ToArray();
        if (names.Any(SensitiveParameters.Contains))
        {
            return target.Uri;
        }

        target.Query = string.Join('&', parameters.Where((_, index) => ReferenceParameters.Contains(names[index])));
        var anchor = uri.Fragment.TrimStart('#');
        // Plain document headings and code-line anchors are useful; OAuth/SPA fragments are not forwarded.
        if (anchor.Length <= 256 && anchor.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
        {
            target.Fragment = anchor;
        }
        return target.Uri;
    }
}
