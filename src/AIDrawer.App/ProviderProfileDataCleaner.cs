using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace AIDrawer;

internal sealed class ProviderProfileDataCleaner(Panel host, CoreWebView2Environment environment)
{
    internal async Task<ProviderProfileCleanupBatchResult> ClearAsync(
        IReadOnlyCollection<ProviderDefinition> providers,
        CoreWebView2BrowsingDataKinds dataKinds)
    {
        var succeeded = new List<string>(providers.Count);
        var failures = new List<ProviderProfileCleanupFailure>();
        foreach (var provider in providers)
        {
            var view = new WebView2 { Visibility = Visibility.Collapsed };
            try
            {
                host.Children.Add(view);
                var options = environment.CreateCoreWebView2ControllerOptions();
                options.ProfileName = provider.ProfileName;
                await view.EnsureCoreWebView2Async(environment, options);
                var profile = view.CoreWebView2?.Profile
                    ?? throw new InvalidOperationException("The provider profile could not be opened.");
                await profile.ClearBrowsingDataAsync(dataKinds);
                succeeded.Add(provider.Id);
            }
            catch (Exception exception)
            {
                failures.Add(new ProviderProfileCleanupFailure(
                    provider.Id,
                    provider.DisplayName,
                    exception.GetType().Name));
            }
            finally
            {
                try
                {
                    view.Close();
                }
                catch
                {
                    // The browser process may already have released the controller.
                }

                host.Children.Remove(view);
            }
        }

        return new ProviderProfileCleanupBatchResult(succeeded, failures);
    }
}

internal sealed record ProviderProfileCleanupBatchResult(
    IReadOnlyList<string> SucceededProviderIds,
    IReadOnlyList<ProviderProfileCleanupFailure> Failures)
{
    internal bool Succeeded => Failures.Count == 0;
}

internal sealed record ProviderProfileCleanupFailure(
    string ProviderId,
    string ProviderName,
    string ErrorType);
