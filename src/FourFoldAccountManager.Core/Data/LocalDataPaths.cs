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
    }

    public string DataRoot { get; }

    public string AccountsFilePath { get; }

    public string SettingsFilePath { get; }

    public string XpTrackerFilePath { get; }

    public string LeaderboardStateFilePath { get; }

    public string WebViewUserDataRoot { get; }
}
