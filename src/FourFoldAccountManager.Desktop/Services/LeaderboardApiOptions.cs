namespace FourFoldAccountManager.Desktop.Services;

public sealed record LeaderboardApiOptions(Uri BaseAddress)
{
    public static LeaderboardApiOptions? FromConfiguredUrl(string? configured)
    {
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
            return null;
        return new LeaderboardApiOptions(uri);
    }

    public static LeaderboardApiOptions? FromEnvironment()
    {
        return FromConfiguredUrl(Environment.GetEnvironmentVariable("FOURFOLD_LEADERBOARD_URL"));
    }
}
