using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Core.Tests;

public sealed class XpTrackerExpansionTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 21, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Level_transition_matches_same_level_rate()
    {
        var crossing = new XpTrackingSession();
        crossing.ApplySnapshot(Snapshot(13, 900, 910), Start);
        crossing.ApplySnapshot(Snapshot(14, 90, 1050), Start.AddMinutes(2));

        var sameLevel = new XpTrackingSession();
        sameLevel.ApplySnapshot(Snapshot(13, 800, 910), Start);
        sameLevel.ApplySnapshot(Snapshot(13, 900, 910), Start.AddMinutes(2));

        Assert.Equal(100, crossing.SessionGain);
        Assert.Equal(sameLevel.RatePerHour, crossing.RatePerHour);
    }

    [Fact]
    public void Multiple_level_transition_counts_all_caps()
    {
        var session = new XpTrackingSession();
        session.ApplySnapshot(Snapshot(13, 900, 910), Start);
        session.ApplySnapshot(Snapshot(15, 20, 1200), Start.AddMinutes(2));

        Assert.Equal(1080, session.SessionGain);
    }

    [Fact]
    public void Time_estimate_uses_remaining_xp_and_rate()
    {
        var session = new XpTrackingSession();
        session.ApplySnapshot(Snapshot(13, 800, 910), Start);
        session.ApplySnapshot(Snapshot(13, 900, 910), Start.AddMinutes(2));

        Assert.Equal(10d / 3000d, session.HoursUntilNextLevel);
    }

    [Fact]
    public void Time_estimate_is_null_without_positive_rate()
    {
        var session = new XpTrackingSession();
        session.ApplySnapshot(Snapshot(13, 900, 910), Start);
        session.ApplySnapshot(Snapshot(13, 900, 910), Start.AddMinutes(2));

        Assert.Null(session.HoursUntilNextLevel);
    }

    [Fact]
    public void Time_estimate_is_null_without_active_class()
    {
        var session = new XpTrackingSession();
        session.ApplySnapshot(Snapshot(13, 800, 910, activeClassName: null), Start);
        session.ApplySnapshot(Snapshot(13, 900, 910, activeClassName: null), Start.AddMinutes(2));

        Assert.Null(session.HoursUntilNextLevel);
    }

    [Fact]
    public void Reset_rate_preserves_session_gain()
    {
        var session = new XpTrackingSession();
        session.ApplySnapshot(Snapshot(13, 800, 910), Start);
        session.ApplySnapshot(Snapshot(13, 900, 910), Start.AddMinutes(2));

        session.ResetRate();

        Assert.Equal(100, session.SessionGain);
        Assert.Empty(session.Intervals);
        Assert.Null(session.RatePerHour);
        Assert.Null(session.HoursUntilNextLevel);
        Assert.Equal(10, session.XpUntilNextLevel);

        session.ApplySnapshot(Snapshot(13, 950, 910), Start.AddMinutes(4));
        Assert.Equal(100, session.SessionGain);
        Assert.Empty(session.Intervals);

        session.ApplySnapshot(Snapshot(13, 1000, 1050), Start.AddMinutes(6));
        Assert.Equal(150, session.SessionGain);
        Assert.Equal(1500, session.RatePerHour);
    }

    [Fact]
    public void Reset_all_clears_session_gain_and_rate()
    {
        var session = new XpTrackingSession();
        session.ApplySnapshot(Snapshot(13, 800, 910), Start);
        session.ApplySnapshot(Snapshot(13, 900, 910), Start.AddMinutes(2));

        session.ResetAll();

        Assert.Equal(0, session.SessionGain);
        Assert.Empty(session.Intervals);
        Assert.Null(session.RatePerHour);
        Assert.Null(session.HoursUntilNextLevel);
        Assert.Equal(10, session.XpUntilNextLevel);
    }

    [Fact]
    public void Reset_isolated_to_one_session()
    {
        var reset = new XpTrackingSession();
        var untouched = new XpTrackingSession();
        foreach (var session in new[] { reset, untouched })
        {
            session.ApplySnapshot(Snapshot(13, 800, 910), Start);
            session.ApplySnapshot(Snapshot(13, 900, 910), Start.AddMinutes(2));
        }

        reset.ResetAll();

        Assert.Equal(0, reset.SessionGain);
        Assert.Null(reset.RatePerHour);
        Assert.Equal(100, untouched.SessionGain);
        Assert.Equal(3000, untouched.RatePerHour);
    }

    private static PlayerProgressSnapshot Snapshot(
        int level,
        long currentXp,
        long nextLevelXp,
        string? activeClassName = "Scout") => new(
            "Desmond",
            activeClassName,
            new Dictionary<string, ClassXpSnapshot>
            {
                ["Scout"] = new(level, currentXp, nextLevelXp, null)
            },
            []);
}
