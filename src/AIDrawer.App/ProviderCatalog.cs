namespace AIDrawer;

internal static class ProviderCatalog
{
    private static readonly IReadOnlySet<string> NoDomains =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyList<string> NoPurchasePaths = [];
    private static readonly IReadOnlyList<string> NoRestorePaths = [];

    internal static readonly IReadOnlyList<ProviderDefinition> AvailableProviders =
    [
        CreateGemini(),
        Create("chatgpt", "ChatGPT", "Experimental", "ms-appx:///Assets/Providers/chatgpt.svg", "C", "https://chatgpt.com/", ["chatgpt.com"], ["auth.openai.com", "accounts.google.com"], ["/c/"]),
        Create("claude", "Claude", "Experimental", "ms-appx:///Assets/Providers/claude.svg", "C", "https://claude.ai/", ["claude.ai"], ["accounts.google.com"], ["/chat/"]),
        Create("grok", "Grok", "Limited", "ms-appx:///Assets/Providers/grok.svg", "G", "https://grok.com/", ["grok.com", "x.com"], NoDomains),
        Create("deepseek", "DeepSeek", "Experimental", "ms-appx:///Assets/Providers/deepseek.svg", "DS", "https://chat.deepseek.com/", ["deepseek.com"], NoDomains),
        Create("doubao", "Doubao / 豆包 (China)", "Experimental", "ms-appx:///Assets/Providers/doubao.png", "豆", "https://www.doubao.com/", ["doubao.com"], ["feishu.cn", "bytedance.com"]),
        Create("qwen", "Qwen Studio (International)", "Experimental", "ms-appx:///Assets/Providers/qwen.png", "QW", "https://chat.qwen.ai/", ["qwen.ai"], NoDomains),
        Create("glm", "GLM / 智谱清言 (China)", "Experimental", "ms-appx:///Assets/Providers/glm.png", "GLM", "https://chatglm.cn/", ["chatglm.cn", "zhipuai.cn"], NoDomains)
    ];

    private static ProviderDefinition Create(
        string id,
        string displayName,
        string compatibilityStatus,
        string? iconAssetUri,
        string iconFallback,
        string homeUrl,
        IEnumerable<string> appDomains,
        IEnumerable<string> authenticationDomains,
        IReadOnlyList<string>? restorePathPrefixes = null) => new(
        id,
        displayName,
        compatibilityStatus,
        iconAssetUri,
        iconFallback,
        new Uri(homeUrl),
        new HashSet<string>(appDomains, StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(authenticationDomains, StringComparer.OrdinalIgnoreCase),
        NoDomains,
        NoPurchasePaths,
        restorePathPrefixes ?? NoRestorePaths);

    private static ProviderDefinition CreateGemini() => new(
        "gemini",
        "Gemini",
        "Experimental",
        "ms-appx:///Assets/Providers/gemini.svg",
        "G",
        new Uri("https://gemini.google.com/"),
        new HashSet<string>(["gemini.google.com"], StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(["accounts.google.com"], StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(["one.google.com", "pay.google.com", "payments.google.com"], StringComparer.OrdinalIgnoreCase),
        ["upgrade", "advanced", "subscription"],
        ["/app/"]);
}
