namespace AIDrawer.Core;

public sealed record WebViewSecuritySettings(
    bool AreDevToolsEnabled,
    bool AreHostObjectsAllowed,
    bool IsWebMessageEnabled,
    bool IsPasswordAutosaveEnabled,
    bool IsGeneralAutofillEnabled);

public static class WebViewSecurityPolicy
{
    public static WebViewSecuritySettings EmbeddedProviderDefaults { get; } = new(
        AreDevToolsEnabled: false,
        AreHostObjectsAllowed: false,
        IsWebMessageEnabled: false,
        IsPasswordAutosaveEnabled: false,
        IsGeneralAutofillEnabled: false);
}
