namespace AIDock.CompatibilityLab;

internal static class SafeEventText
{
    public static string SchemeCategory(string? rawUri)
    {
        if (!Uri.TryCreate(rawUri, UriKind.Absolute, out var uri))
        {
            return "invalid";
        }

        return uri.Scheme is "http" or "https" ? uri.Scheme : "unsupported";
    }
}
