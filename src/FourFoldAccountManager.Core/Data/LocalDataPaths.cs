namespace FourFoldAccountManager.Core.Data;

public sealed class LocalDataPaths
{
    public LocalDataPaths(string? root = null)
    {
        var dataRoot = string.IsNullOrWhiteSpace(root)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FourFoldAccountManager")
            : root;

        DataRoot = Path.GetFullPath(dataRoot);
        AccountsFilePath = Path.Combine(DataRoot, "accounts.json");
        SettingsFilePath = Path.Combine(DataRoot, "settings.json");
        XpTrackerFilePath = Path.Combine(DataRoot, "xp-tracker.json");
        LeaderboardStateFilePath = Path.Combine(DataRoot, "leaderboard-state.json");
        WebViewUserDataRoot = Path.Combine(DataRoot, "WebView2");
        DevPluginsRoot = Path.Combine(DataRoot, "dev-plugins");
        PluginDataRoot = Path.Combine(DataRoot, "plugin-data");
        PluginWebViewUserDataRoot = Path.Combine(DataRoot, "PluginWebView2");
        HubPluginsRoot = Path.Combine(DataRoot, "plugins");
        HubCatalogFilePath = Path.Combine(DataRoot, "hub", "catalog.json");
        HubInstalledFilePath = Path.Combine(DataRoot, "hub", "installed.json");
    }

    public string DataRoot { get; }

    public string AccountsFilePath { get; }

    public string SettingsFilePath { get; }

    public string XpTrackerFilePath { get; }

    public string LeaderboardStateFilePath { get; }

    public string WebViewUserDataRoot { get; }

    // Plugin authors' work-in-progress plugins, loaded while developer mode is on.
    public string DevPluginsRoot { get; }

    // One JSON file of private storage per community plugin.
    public string PluginDataRoot { get; }

    // The plugin browser's own data folder, separate from the game profiles in WebViewUserDataRoot.
    public string PluginWebViewUserDataRoot { get; }

    // Plugins installed from the hub, one folder per plugin id.
    public string HubPluginsRoot { get; }

    // The last good copy of the hub's catalog, so installed plugins work offline.
    public string HubCatalogFilePath { get; }

    // Which hub plugins are installed, and at which reviewed commit.
    public string HubInstalledFilePath { get; }
}
