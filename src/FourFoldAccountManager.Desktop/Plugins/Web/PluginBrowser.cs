using System.IO;
using System.Text.Json;
using System.Windows.Controls;
using FourFoldAccountManager.Core.Data;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace FourFoldAccountManager.Desktop.Plugins.Web;

// The one WebView2 environment every community plugin runs in. Its data folder is separate from the games', so a
// plugin can never see a game's cookies, storage or pages.
public sealed class PluginBrowser(LocalDataPaths paths)
{
    private Task<CoreWebView2Environment>? _environment;

    public async Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        Directory.CreateDirectory(paths.PluginWebViewUserDataRoot);
        var environment = _environment ??= CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null, userDataFolder: paths.PluginWebViewUserDataRoot);
        try
        {
            return await environment;
        }
        catch
        {
            // Let a later start try again instead of caching the failure.
            if (environment.IsFaulted || environment.IsCanceled)
            {
                _environment = null;
            }

            throw;
        }
    }

    // Removes everything the plugin browser stored for one plugin's address (cookies, IndexedDB, caches), after the
    // plugin is uninstalled. It needs a web view to ask through, so it borrows a blank one for a moment. Best effort:
    // data left behind takes disk space and nothing else.
    public async Task ClearOriginAsync(Panel parkingHost, Uri origin)
    {
        // No plugin has ever run, so there is nothing to clear and no reason to start a browser.
        if (!Directory.Exists(paths.PluginWebViewUserDataRoot))
        {
            return;
        }

        WebView2CompositionControl? view = null;
        try
        {
            view = new WebView2CompositionControl();
            parkingHost.Children.Add(view);
            await view.EnsureCoreWebView2Async(await GetEnvironmentAsync());
            await view.CoreWebView2.CallDevToolsProtocolMethodAsync(
                "Storage.clearDataForOrigin",
                JsonSerializer.Serialize(new { origin = origin.GetLeftPart(UriPartial.Authority), storageTypes = "all" }));
        }
        catch (Exception)
        {
            // Left behind; see above.
        }
        finally
        {
            // Null when the control could not even be created.
            if (view is not null)
            {
                parkingHost.Children.Remove(view);
                view.Dispose();
            }
        }
    }
}
