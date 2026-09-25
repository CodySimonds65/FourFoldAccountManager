using FourFoldAccountManager.Core.Leaderboard;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Leaderboard;

public sealed class LeaderboardPeriodWindowTests
{
    [Fact]
    public void DailyWindowUsesUtcMidnightBoundaries()
    {
        var window = LeaderboardPeriodWindow.GetCurrent(
            LeaderboardPeriod.Daily,
            new DateTimeOffset(2026, 4, 15, 18, 22, 0, TimeSpan.Zero));

        Assert.Equal(Utc(2026, 4, 15), window.StartUtc);
        Assert.Equal(Utc(2026, 4, 16), window.EndUtc);
    }

    [Fact]
    public void WeeklyWindowStartsOnMonday()
    {
        var window = LeaderboardPeriodWindow.GetCurrent(
            LeaderboardPeriod.Weekly,
            new DateTimeOffset(2026, 4, 19, 23, 59, 59, TimeSpan.Zero));

        Assert.Equal(Utc(2026, 4, 13), window.StartUtc);
        Assert.Equal(Utc(2026, 4, 20), window.EndUtc);
    }

    [Fact]
    public void DecemberJanuaryDateUsesThePriorIsoWeekYear()
    {
        var window = LeaderboardPeriodWindow.GetCurrent(
            LeaderboardPeriod.Weekly,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(Utc(2025, 12, 29), window.StartUtc);
        Assert.Equal(Utc(2026, 1, 5), window.EndUtc);
    }

    [Fact]
    public void ExactExclusivePeriodEndBelongsToNextPeriod()
    {
        var window = LeaderboardPeriodWindow.GetCurrent(
            LeaderboardPeriod.Daily,
            Utc(2026, 4, 16));

        Assert.Equal(Utc(2026, 4, 16), window.StartUtc);
        Assert.Equal(Utc(2026, 4, 17), window.EndUtc);
    }

    [Fact]
    public void ContractsExposeTheSharedLeaderboardPayloadShape()
    {
        var profile = new LeaderboardProfile(277, "Player");
        var heartbeat = new ParticipationHeartbeat(Guid.NewGuid(), true, [profile], [277]);
        var entry = new LeaderboardEntry(1, profile.PlayerId, profile.Username, 125, Utc(2026, 4, 15), false);
        var page = new LeaderboardPage(LeaderboardPeriod.Daily, Utc(2026, 4, 15), Utc(2026, 4, 16),
            1, 50, 1, Utc(2026, 4, 15), [entry]);

        Assert.True(heartbeat.SharingEnabled);
        Assert.Equal(profile, Assert.Single(heartbeat.LinkedProfiles));
        Assert.Equal([277], heartbeat.ActivePlayerIds);
        Assert.Equal(LeaderboardPeriod.Daily, page.Period);
        Assert.Equal(entry, Assert.Single(page.Entries));
        Assert.Equal(0, page.PendingBaselineProfiles);
    }

    private static DateTimeOffset Utc(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, TimeSpan.Zero);
}
