namespace FourFoldAccountManager.Leaderboard.Service.Collection;

public sealed class LeaderboardCollectionOptions
{
    public const string SectionName = "Collection";
    public bool Enabled { get; set; }
    // Per-player cadence: each active player's public profile is requested at most this often.
    public TimeSpan MinimumSampleInterval { get; set; }
    // Global spacing between consecutive public-profile requests from this service.
    public TimeSpan RequestSpacing { get; set; } = TimeSpan.FromSeconds(2);
    public string? ProfileUrlTemplate { get; set; }
    public TimeSpan ActiveLeaseDuration { get; set; } = TimeSpan.FromMinutes(3);
    public int RetentionDays { get; set; } = 40;

    public bool CanCollect => Enabled && MinimumSampleInterval > TimeSpan.Zero && RequestSpacing >= TimeSpan.Zero;
}
