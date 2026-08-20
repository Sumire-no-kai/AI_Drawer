namespace AIDock.CompatibilityLab;

internal static class ProviderCatalog
{
    private static readonly IReadOnlySet<string> NoPurchaseHosts =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyList<string> NoPurchasePathFragments = [];

    internal static readonly IReadOnlyList<ProviderDefinition> InitialCandidates =
    [
        Create("chatgpt", "ChatGPT", "https://chatgpt.com/"),
        Create("claude", "Claude", "https://claude.ai/"),
        CreateGemini(),
        Create("grok", "Grok", "https://grok.com/"),
        Create("deepseek", "DeepSeek", "https://chat.deepseek.com/"),
        Create("doubao", "Doubao / 豆包", "https://www.doubao.com/"),
        Create("qwen", "Qwen / 通义千问", "https://chat.qwen.ai/"),
        Create("glm", "GLM / 智谱清言", "https://chatglm.cn/")
    ];

    private static ProviderDefinition Create(string id, string displayName, string homeUrl) => new(
        id,
        displayName,
        "Not tested candidate",
        new Uri(homeUrl),
        NoPurchaseHosts,
        NoPurchasePathFragments);

    private static ProviderDefinition CreateGemini() => new(
        "gemini",
        "Gemini",
        "Experimental",
        new Uri("https://gemini.google.com/"),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pay.google.com",
            "payments.google.com",
            "one.google.com"
        },
        ["upgrade", "advanced", "subscription"]);
}
