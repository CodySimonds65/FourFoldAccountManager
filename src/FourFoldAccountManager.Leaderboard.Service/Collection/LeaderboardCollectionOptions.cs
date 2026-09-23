namespace FourFoldAccountManager.Leaderboard.Service.Collection;

public sealed class LeaderboardCollectionOptions
{
    public const string SectionName = "Collection";
    public bool Enabled { get; set; }
    public TimeSpan MinimumSampleInterval { get; set; }
    public string? ProfileUrlTemplate { get; set; }

    public bool CanCollect => Enabled && MinimumSampleInterval > TimeSpan.Zero;
}
