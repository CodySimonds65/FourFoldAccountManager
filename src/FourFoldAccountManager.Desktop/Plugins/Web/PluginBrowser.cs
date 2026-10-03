using System.IO;
using FourFoldAccountManager.Core.Data;
using Microsoft.Web.WebView2.Core;

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
}
