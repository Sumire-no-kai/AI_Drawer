namespace AIDrawer;

internal static class ProviderCatalog
{
    private static readonly IReadOnlySet<string> NoDomains =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyList<string> NoPurchasePaths = [];

    internal static readonly IReadOnlyList<ProviderDefinition> AvailableProviders =
    [
        CreateGemini(),
        Create("chatgpt", "ChatGPT", "Experimental", "https://chatgpt.com/", ["chatgpt.com"], ["auth.openai.com", "accounts.google.com"]),
        Create("claude", "Claude", "Experimental", "https://claude.ai/", ["claude.ai"], ["accounts.google.com"]),
        Create("grok", "Grok", "Limited", "https://grok.com/", ["grok.com", "x.com"], NoDomains),
        Create("deepseek", "DeepSeek", "Experimental", "https://chat.deepseek.com/", ["deepseek.com"], NoDomains),
        Create("doubao", "Doubao / 豆包 (China)", "Experimental", "https://www.doubao.com/", ["doubao.com"], ["feishu.cn", "bytedance.com"]),
        Create("qwen", "Qwen Studio (International)", "Experimental", "https://chat.qwen.ai/", ["qwen.ai"], NoDomains),
        Create("glm", "GLM / 智谱清言 (China)", "Experimental", "https://chatglm.cn/", ["chatglm.cn", "zhipuai.cn"], NoDomains)
    ];

    private static ProviderDefinition Create(
        string id,
        string displayName,
        string compatibilityStatus,
        string homeUrl,
        IEnumerable<string> appDomains,
        IEnumerable<string> authenticationDomains) => new(
        id,
        displayName,
        compatibilityStatus,
        new Uri(homeUrl),
        new HashSet<string>(appDomains, StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(authenticationDomains, StringComparer.OrdinalIgnoreCase),
        NoDomains,
        NoPurchasePaths);

    private static ProviderDefinition CreateGemini() => new(
        "gemini",
        "Gemini",
        "Experimental",
        new Uri("https://gemini.google.com/"),
        new HashSet<string>(["gemini.google.com"], StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(["accounts.google.com"], StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(["one.google.com", "pay.google.com", "payments.google.com"], StringComparer.OrdinalIgnoreCase),
        ["upgrade", "advanced", "subscription"]);
}
