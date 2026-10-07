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
        session.EndLive();
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
}
