namespace AIDock.CompatibilityLab;

internal static class ProviderCatalog
{
    private static readonly IReadOnlySet<string> NoPurchaseHosts =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyList<string> NoPurchasePathFragments = [];

    internal static readonly IReadOnlyList<ProviderDefinition> InitialCandidates =
    [
        Create("chatgpt", "ChatGPT", "Experimental", "https://chatgpt.com/"),
        Create("claude", "Claude", "Experimental", "https://claude.ai/"),
        CreateGemini(),
        Create("grok", "Grok", "Limited", "https://grok.com/"),
        Create("deepseek", "DeepSeek", "Experimental", "https://chat.deepseek.com/"),
        Create("doubao", "Doubao / 豆包 (China)", "Experimental", "https://www.doubao.com/"),
        Create("qwen", "Qwen Studio (International)", "Experimental", "https://chat.qwen.ai/"),
        Create("qwen-cn", "Tongyi Qianwen / 通义千问 (China)", "Not tested candidate", "https://tongyi.aliyun.com/qianwen/"),
        Create("glm", "GLM / 智谱清言 (China)", "Experimental", "https://chatglm.cn/"),
        Create("zai", "Z.ai (International)", "Not tested candidate", "https://chat.z.ai/"),
        Create("copilot", "Microsoft Copilot (Personal)", "Not tested candidate", "https://copilot.microsoft.com/")
    ];

    private static ProviderDefinition Create(
        string id,
        string displayName,
        string compatibilityStatus,
        string homeUrl) => new(
        id,
        displayName,
        compatibilityStatus,
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
