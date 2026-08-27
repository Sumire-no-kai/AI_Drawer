namespace AIDrawer;

internal static class ProviderCatalog
{
    private static readonly Uri? IsolatedTestHomeUri = ResolveIsolatedTestHomeUri();
    private static readonly IReadOnlySet<string> NoDomains =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyList<string> NoPurchasePaths = [];
    private static readonly IReadOnlyList<string> NoRestorePaths = [];

    internal static readonly IReadOnlyList<ProviderDefinition> AvailableProviders =
    [
        CreateGemini(),
        Create("chatgpt", "ChatGPT", "Experimental", "https://chatgpt.com/", ["chatgpt.com"], ["auth.openai.com", "accounts.google.com"], ["/c/"]),
        Create("claude", "Claude", "Experimental", "https://claude.ai/", ["claude.ai"], ["accounts.google.com"], ["/chat/"]),
        Create("grok", "Grok", "Limited", "https://grok.com/", ["grok.com", "x.com"], NoDomains),
        Create("deepseek", "DeepSeek", "Experimental", "https://chat.deepseek.com/", ["chat.deepseek.com"], NoDomains),
        Create("doubao", "Doubao / 豆包 (China)", "Experimental", "https://www.doubao.com/", ["www.doubao.com"], ["feishu.cn", "bytedance.com"]),
        Create("qwen", "Qwen Studio (International)", "Experimental", "https://chat.qwen.ai/", ["chat.qwen.ai"], NoDomains),
        Create("glm", "GLM / 智谱清言 (China)", "Experimental", "https://chatglm.cn/", ["chatglm.cn", "zhipuai.cn"], NoDomains),
        Create("copilot", "Microsoft Copilot (Personal)", "Experimental", "https://copilot.microsoft.com/", ["copilot.microsoft.com", "copilot.com"], NoDomains)
    ];

    private static ProviderDefinition Create(
        string id,
        string displayName,
        string compatibilityStatus,
        string homeUrl,
        IEnumerable<string> appDomains,
        IEnumerable<string> authenticationDomains,
        IReadOnlyList<string>? restorePathPrefixes = null)
    {
        var resolvedHomeUri = IsolatedTestHomeUri ?? new Uri(homeUrl);
        return new ProviderDefinition(
            id,
            displayName,
            compatibilityStatus,
            resolvedHomeUri,
            IsolatedTestHomeUri is null
                ? new HashSet<string>(appDomains, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>([resolvedHomeUri.IdnHost], StringComparer.OrdinalIgnoreCase),
            IsolatedTestHomeUri is null
                ? new HashSet<string>(authenticationDomains, StringComparer.OrdinalIgnoreCase)
                : NoDomains,
            NoDomains,
            NoPurchasePaths,
            restorePathPrefixes ?? NoRestorePaths);
    }

    private static ProviderDefinition CreateGemini()
    {
        var homeUri = IsolatedTestHomeUri ?? new Uri("https://gemini.google.com/");
        return new ProviderDefinition(
            "gemini",
            "Gemini",
            "Experimental",
            homeUri,
            new HashSet<string>([homeUri.IdnHost], StringComparer.OrdinalIgnoreCase),
            IsolatedTestHomeUri is null
                ? new HashSet<string>(["accounts.google.com"], StringComparer.OrdinalIgnoreCase)
                : NoDomains,
            new HashSet<string>(["one.google.com", "pay.google.com", "payments.google.com"], StringComparer.OrdinalIgnoreCase),
            ["upgrade", "advanced", "subscription"],
            IsolatedTestHomeUri is null ? ["/app/"] : ["/"]);
    }

    private static Uri? ResolveIsolatedTestHomeUri()
    {
#if DEBUG
        var testDataRoot = Environment.GetEnvironmentVariable("AI_DRAWER_TEST_DATA_ROOT");
        var rawUri = Environment.GetEnvironmentVariable("AI_DRAWER_TEST_PROVIDER_ORIGIN");
        if (Program.IsIsolatedRuntimeAcceptanceRoot(testDataRoot)
            && Uri.TryCreate(rawUri, UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(uri.UserInfo)
            && uri.IsDefaultPort
            && uri.AbsolutePath == "/"
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment))
        {
            return uri;
        }
#endif

        return null;
    }
}
