namespace AIDock.CompatibilityLab;

internal static class SafeEventText
{
    public static string Origin(string? rawUri)
    {
        if (!Uri.TryCreate(rawUri, UriKind.Absolute, out var uri))
        {
            return "invalid-or-empty-origin";
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return $"{uri.Scheme}://";
        }

        var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
        return $"{uri.Scheme}://{uri.IdnHost}{port}";
    }
}
