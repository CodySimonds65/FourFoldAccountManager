using FourFoldAccountManager.Core.Tracking;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Tracking;

// The live game feed feeding the XP tracker. A wrong count here is wrong XP/hr and session XP in the tracker panel,
// the calculator, every overlay card and every plugin.
public sealed class XpTrackingSessionLiveTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(double minutes) => Start.AddMinutes(minutes);

    private static long Cap(int level) => 5L * level * (level + 1);

    // A polled profile: Savage at the given level and XP, and a Mage that never moves.
    private static PlayerProgressSnapshot Poll(string active, int savageLevel, long savageXp) =>
        new("cody", active,
            new Dictionary<string, ClassProfileSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                ["Savage"] = new(savageLevel, savageXp, Cap(savageLevel), null),
                ["Mage"] = new(3, 0, Cap(3), null)
            },
            []);

    [Fact]
    public void AFightAndAPollInTheSameWindowCountTheXpOnce()
    {
        var session = new XpTrackingSession();
        session.ApplySnapshot(Poll("Savage", 12, 100), At(0));
        session.BeginLive(At(0.1));
        session.ApplyLiveResult(At(0.5), "Savage", 300, 12, Cap(12) - 400);
        session.ApplySnapshot(Poll("Savage", 12, 400), At(1)); // the same fight, as the poll saw it
        session.ApplyLiveResult(At(1.5), "Savage", 300, 12, Cap(12) - 700);
        session.ApplySnapshot(Poll("Savage", 12, 700), At(2));

        Assert.Equal(600, session.SessionGain);
        // 600 over the 1.9 watched minutes (0.1 to 2), the last half minute idle.
        Assert.Equal(600 / 1.9 * 60, session.RatePerHour!.Value, 6);
    }

    [Fact]
    public void AWatchThatBeganBeforeTheLastPollCountsOnlyFightsAfterIt()
    {
        var session = new XpTrackingSession();
        session.ApplySnapshot(Poll("Savage", 12, 100), At(0));
        session.ApplySnapshot(Poll("Savage", 12, 400), At(1)); // 300 counted from 0 to 1
        session.BeginLive(At(0.5)); // the socket opened before that poll
        session.ApplyLiveResult(At(0.75), "Savage", 300, 12, Cap(12) - 400); // already in the poll
        session.ApplyLiveResult(At(1.5), "Savage", 200, 12, Cap(12) - 600);

        Assert.Equal(500, session.SessionGain);
    }

    [Fact]
    public void TheFirstPollAfterALiveStretchIsABaseline()
    {
        var session = new XpTrackingSession();
        session.ApplySnapshot(Poll("Savage", 12, 100), At(0));
        session.BeginLive(At(0));
        session.ApplyLiveResult(At(0.5), "Savage", 300, 12, Cap(12) - 400);
        session.EndLive(At(0.75));
        session.ApplySnapshot(Poll("Savage", 12, 400), At(1)); // the fight's XP, already counted

        Assert.False(session.IsLive);
        Assert.Equal(300, session.SessionGain);

        session.ApplySnapshot(Poll("Savage", 12, 450), At(2));
        Assert.Equal(350, session.SessionGain);
    }

    [Fact]
    public void TheLiveRateWaitsForAMinuteAndFallsWhileIdle()
    {
        var session = new XpTrackingSession();
        session.ApplySnapshot(Poll("Savage", 12, 100), At(0));
        session.BeginLive(At(0));
        session.ApplyLiveResult(At(0.5), "Savage", 300, 12, Cap(12) - 400);
        Assert.Null(session.RatePerHour); // 30 s watched

        session.ApplySnapshot(Poll("Savage", 12, 400), At(1)); // a minute watched, the last 30 s idle
        Assert.Equal(18000, session.RatePerHour!.Value, 6);

        session.ApplySnapshot(Poll("Savage", 12, 400), At(3)); // two more idle minutes
        Assert.Equal(6000, session.RatePerHour!.Value, 6);
    }

    [Fact]
    public void ARepeatedOrLateFightAddsNothing()
    {
        // No poll at all: an account without a ranking username still gets a live rate from its fights.
        var session = new XpTrackingSession();
        session.BeginLive(At(0));
        session.ApplyLiveResult(At(1), "Savage", 300, 12, 100);
        session.ApplyLiveResult(At(1), "Savage", 300, 12, 100);
        session.ApplyLiveResult(At(0.5), "Savage", 300, 12, 100);

        Assert.Equal(300, session.SessionGain);
        Assert.Equal(18000, session.RatePerHour!.Value, 6);
    }

    [Fact]
    public void AFightGivesTheExactProgressAcrossALevelUp()
    {
        var session = new XpTrackingSession();
        session.ApplySnapshot(Poll("Savage", 12, Cap(12) - 50), At(0));
        session.BeginLive(At(0));
        // The feed spells the class its own way; it must update the website's entry, not add a second one.
        session.ApplyLiveResult(At(0.5), " savage", 200, 13, Cap(13) - 150);

        var display = session.DisplaySnapshot!;
        var savage = display.Classes["Savage"];
        Assert.Equal((13, 150L, Cap(13)), (savage.Level, savage.CurrentXp, savage.NextLevelXp));
        Assert.Equal("Savage", display.ActiveClassName);
        Assert.Equal(2, display.Classes.Count);
        Assert.Equal(Cap(13) - 150, session.XpUntilNextLevel);
        // The raw poll is untouched: persistence and the reconciler read it.
        Assert.Equal(12, session.LastSnapshot!.Classes["Savage"].Level);
    }

    [Fact]
    public void AVeryHighLevelCapsTheLevelTotalLikeTheGame()
    {
        var session = new XpTrackingSession();
        session.ApplySnapshot(Poll("Savage", 12, 100), At(0));
        session.BeginLive(At(0));
        session.ApplyLiveResult(At(0.5), "Savage", 1, 30000, 10);

        var savage = session.DisplaySnapshot!.Classes["Savage"];
        Assert.Equal(int.MaxValue, savage.NextLevelXp);
        Assert.Equal(int.MaxValue - 10L, savage.CurrentXp);
    }

    [Fact]
    public void ALaggingPollKeepsTheLiveProgressAndALaterOneReplacesIt()
    {
        var session = new XpTrackingSession();
        session.ApplySnapshot(Poll("Savage", 12, 100), At(0));
        session.BeginLive(At(0));
        session.ApplyLiveResult(At(0.9), "Savage", 300, 12, Cap(12) - 400);

        // Saved just before the fight landed: the display must not roll back.
        session.ApplySnapshot(Poll("Savage", 12, 100), At(1));
        Assert.Equal(400, session.DisplaySnapshot!.Classes["Savage"].CurrentXp);

        // A second poll since the fight is the truth, even below the live value.
        session.ApplySnapshot(Poll("Savage", 12, 350), At(2));
        Assert.Equal(350, session.DisplaySnapshot!.Classes["Savage"].CurrentXp);
    }

    [Fact]
    public void ANewerPollAheadOfTheFightOrOnAnotherClassReplacesTheLiveProgress()
    {
        var ahead = new XpTrackingSession();
        ahead.ApplySnapshot(Poll("Savage", 12, 100), At(0));
        ahead.BeginLive(At(0));
        ahead.ApplyLiveResult(At(0.9), "Savage", 300, 12, Cap(12) - 400);
        ahead.ApplySnapshot(Poll("Savage", 12, 450), At(1));
        Assert.Equal(450, ahead.DisplaySnapshot!.Classes["Savage"].CurrentXp);

        // A class switch in town, with no fight since: the poll's active class wins.
        var switched = new XpTrackingSession();
        switched.ApplySnapshot(Poll("Savage", 12, 100), At(0));
        switched.BeginLive(At(0));
        switched.ApplyLiveResult(At(0.5), "Savage", 300, 12, Cap(12) - 400);
        switched.ApplySnapshot(Poll("Mage", 12, 400), At(1));
        Assert.Equal("Mage", switched.ActiveClassName);
        Assert.Equal(Cap(3), switched.XpUntilNextLevel);
    }

    [Fact]
    public void TheIdleStretchBeforeALiveStretchEndsStaysInTheRate()
    {
        var session = new XpTrackingSession();
        session.ApplySnapshot(Poll("Savage", 12, 100), At(0));
        session.BeginLive(At(0));
        session.ApplyLiveResult(At(1), "Savage", 300, 12, Cap(12) - 400);
        session.EndLive(At(5)); // four idle minutes, then a disconnect
        session.ApplySnapshot(Poll("Savage", 12, 400), At(6));

        // 300 over the 5 watched minutes, not over the 1 minute of the fight.
        Assert.Equal(3600, session.RatePerHour!.Value, 6);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AResetWhileLiveStartsTheRateFromThePress(bool all)
    {
        var session = new XpTrackingSession();
        session.ApplySnapshot(Poll("Savage", 12, 100), At(0));
        session.BeginLive(At(0));
        session.ApplyLiveResult(At(1), "Savage", 200, 12, Cap(12) - 300);
        if (all) session.ResetAll(At(10)); else session.ResetRate(At(10));
        session.ApplyLiveResult(At(11), "Savage", 300, 12, Cap(12) - 600);
        session.ApplySnapshot(Poll("Savage", 12, 600), At(12));

        Assert.True(session.IsLive);
        // 300 over the 2 watched minutes since the press.
        Assert.Equal(9000, session.RatePerHour!.Value, 6);
        Assert.Equal(all ? 300 : 500, session.SessionGain);
    }

    [Fact]
    public void ALaggingPollAfterALiveStretchDoesNotCountTheFightAgain()
    {
        var session = new XpTrackingSession();
        session.ApplySnapshot(Poll("Savage", 12, 100), At(0));
        session.BeginLive(At(0));
        session.ApplyLiveResult(At(0.9), "Savage", 300, 12, Cap(12) - 400);
        session.EndLive(At(0.95));
        session.ApplySnapshot(Poll("Savage", 12, 100), At(1)); // saved just before the fight landed
        session.ApplySnapshot(Poll("Savage", 12, 400), At(2)); // the same fight, which the live stretch counted

        Assert.Equal(300, session.SessionGain);
    }

    [Fact]
    public void TheRateShownNowFallsBetweenFightsAndStaysPutWhenPolled()
    {
        var live = new XpTrackingSession();
        live.ApplySnapshot(Poll("Savage", 12, 100), At(0));
        live.BeginLive(At(0));
        Assert.Null(live.RateNow(At(0.5))); // under a minute watched
        live.ApplyLiveResult(At(1), "Savage", 300, 12, Cap(12) - 400);
        Assert.Equal(18000, live.RateNow(At(1))!.Value, 6);
        // Going off live keeps the fallen rate instead of jumping back to the one at the last fight.
        var ended = new XpTrackingSession();
        ended.ApplySnapshot(Poll("Savage", 12, 100), At(0));
        ended.BeginLive(At(0));
        ended.ApplyLiveResult(At(1), "Savage", 300, 12, Cap(12) - 400);
        ended.EndLive(At(3));
        Assert.Equal(6000, ended.RateNow(At(3))!.Value, 6);
        Assert.Equal(6000, live.RateNow(At(3))!.Value, 6); // two idle minutes, no poll yet

        var polled = new XpTrackingSession();
        polled.ApplySnapshot(Poll("Savage", 12, 100), At(0));
        polled.ApplySnapshot(Poll("Savage", 12, 400), At(1));
        Assert.Equal(polled.RatePerHour, polled.RateNow(At(30)));
    }
}
