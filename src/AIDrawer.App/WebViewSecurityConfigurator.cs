using AIDrawer.Core;
using Microsoft.Web.WebView2.Core;

namespace AIDrawer;

internal static class WebViewSecurityConfigurator
{
    internal static void Apply(CoreWebView2Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var policy = WebViewSecurityPolicy.EmbeddedProviderDefaults;
        settings.AreDevToolsEnabled = policy.AreDevToolsEnabled;
        settings.AreHostObjectsAllowed = policy.AreHostObjectsAllowed;
        settings.IsWebMessageEnabled = policy.IsWebMessageEnabled;
        settings.IsPasswordAutosaveEnabled = policy.IsPasswordAutosaveEnabled;
        settings.IsGeneralAutofillEnabled = policy.IsGeneralAutofillEnabled;
    }
}
