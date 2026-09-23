namespace FourFoldAccountManager.Desktop.Services;

public sealed record LeaderboardApiOptions(Uri BaseAddress)
{
    public static LeaderboardApiOptions? FromEnvironment()
    {
        var configured = Environment.GetEnvironmentVariable("FOURFOLD_LEADERBOARD_URL");
        return Uri.TryCreate(configured, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? new LeaderboardApiOptions(uri)
            : null;
    }
}
