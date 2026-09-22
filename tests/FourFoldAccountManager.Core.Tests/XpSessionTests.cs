using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Core.Tests;

public class XpSessionTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 21, 0, 0, 0, TimeSpan.Zero);

    private static PlayerProgressSnapshot Snapshot(long xp) => new(
        "Desmond", "Scout", new Dictionary<string, ClassXpSnapshot>
        {
            ["Scout"] = new(13, xp, 910, null)
        }, []);

    [Fact]
    public void First_sample_is_baseline_and_second_sets_rate_and_remaining_xp()
    {
        var session = new XpTrackingSession();
        session.ApplySnapshot(Snapshot(700), Start);
        Assert.Null(session.RatePerHour);
        Assert.Equal(210, session.XpUntilNextLevel);
        session.ApplySnapshot(Snapshot(800), Start.AddMinutes(2));
        Assert.Equal(3000, session.RatePerHour);
        Assert.Equal(100, session.SessionGain);
        Assert.Equal(110, session.XpUntilNextLevel);
        Assert.False(session.IsStale);
    }

    [Fact]
    public void Failure_marks_only_its_session_stale_and_keeps_rate()
    {
        var failed = new XpTrackingSession();
        var healthy = new XpTrackingSession();
        foreach (var session in new[] { failed, healthy })
        {
            session.ApplySnapshot(Snapshot(700), Start);
            session.ApplySnapshot(Snapshot(800), Start.AddMinutes(2));
        }
        failed.MarkFetchFailed(Start.AddMinutes(3));
        Assert.True(failed.IsStale);
        Assert.Equal(3000, failed.RatePerHour);
        Assert.False(healthy.IsStale);
        Assert.Equal(Start.AddMinutes(2), failed.LastSuccessfulAt);
    }

    [Fact]
    public void Stop_ends_only_selected_session()
    {
        var stopped = new XpTrackingSession();
        var other = new XpTrackingSession();
        stopped.Stop();
        Assert.True(stopped.IsStopped);
        Assert.False(other.IsStopped);
    }
}
