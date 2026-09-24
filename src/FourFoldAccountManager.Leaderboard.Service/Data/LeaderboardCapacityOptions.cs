namespace FourFoldAccountManager.Leaderboard.Service.Data;

public sealed class LeaderboardCapacityOptions
{
    public const string SectionName = "Capacity";
    public int MaxInstallations { get; set; } = 5_000;
    public int MaxProfileLinks { get; set; } = 50_000;
}
