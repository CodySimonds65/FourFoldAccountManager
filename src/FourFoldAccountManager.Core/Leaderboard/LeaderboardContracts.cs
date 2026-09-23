namespace FourFoldAccountManager.Core.Leaderboard;

public enum LeaderboardPeriod
{
    Daily,
    Weekly,
    Monthly
}

public sealed record LeaderboardProfile(int PlayerId, string Username);

public sealed record ParticipationHeartbeat(
    Guid InstallationId,
    bool SharingEnabled,
    IReadOnlyList<LeaderboardProfile> LinkedProfiles,
    IReadOnlyList<int> ActivePlayerIds);

public sealed record LeaderboardEntry(
    int Rank,
    int PlayerId,
    string Username,
    long XpGained,
    DateTimeOffset LastSampledAtUtc,
    bool IsStale);

public sealed record LeaderboardPage(
    LeaderboardPeriod Period,
    DateTimeOffset PeriodStartUtc,
    DateTimeOffset PeriodEndUtc,
    int Page,
    int PageSize,
    int TotalEntries,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<LeaderboardEntry> Entries);
