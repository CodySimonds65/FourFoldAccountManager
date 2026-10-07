using FourFoldAccountManager.Core.LiveFeed;
using FourFoldAccountManager.Core.Tracking;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.LiveFeed;

// The reconciler's call to give up on the feed. Missing it leaves every account live with no battle results, so XP/hr
// decays to 0 while the account farms; a false call drops the feed for a timing quirk.
public sealed class XpReconcilerTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Account = Guid.NewGuid();

    private static DateTimeOffset At(double minutes) => Start.AddMinutes(minutes);

    private static long Cap(int level) => 5L * level * (level + 1);

    // A polled profile: Savage active at level 12 with the given XP, and a Mage that never moves.
    private static PlayerProgressSnapshot Poll(long savageXp) =>
        new("cody", "Savage",
            new Dictionary<string, ClassProfileSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                ["Savage"] = new(12, savageXp, Cap(12), null),
                ["Mage"] = new(3, 0, Cap(3), null)
            },
            []);

    private static bool Sample(XpReconciler reconciler, long savageXp, double minutes) =>
        reconciler.RecordSample(Account, Poll(savageXp), At(minutes), At(-1));

    [Fact]
    public void TwoWatchedWindowsWithXpAndNoResultsGiveUpOnTheFeed()
    {
        var reconciler = new XpReconciler();
        Sample(reconciler, 100, 0);

        Assert.False(Sample(reconciler, 200, 1));
        Assert.True(Sample(reconciler, 300, 2));
    }

    [Fact]
    public void AMatchingResultInBetweenStartsTheCountAgain()
    {
        var reconciler = new XpReconciler();
        Sample(reconciler, 100, 0);
        Assert.False(Sample(reconciler, 200, 1));
        reconciler.RecordResult(Account, At(1.5), "Savage", 100);
        Assert.False(Sample(reconciler, 300, 2));

        Assert.False(Sample(reconciler, 400, 3));
        Assert.True(Sample(reconciler, 500, 4));
    }

    [Fact]
    public void ABattleCountedAWindowLateNeverGivesUpOnTheFeed()
    {
        var reconciler = new XpReconciler();
        Sample(reconciler, 100, 0);
        // The poll sees the XP, the result lands just after it: +x, then -x. Then the other way round.
        Assert.False(Sample(reconciler, 200, 1));
        reconciler.RecordResult(Account, At(1.01), "Savage", 100);
        Assert.False(Sample(reconciler, 200, 2));
        reconciler.RecordResult(Account, At(2.99), "Savage", 100);
        Assert.False(Sample(reconciler, 200, 3));
        Assert.False(Sample(reconciler, 300, 4));

        Assert.Equal((4, 0), (reconciler.Matched, reconciler.Mismatched));
    }

    [Fact]
    public void AnEarlyPairThenALatePairNeverGivesUpOnTheFeed()
    {
        var reconciler = new XpReconciler();
        Sample(reconciler, 100, 0);
        // The result lands just before the poll and the XP just after it: the XP shows a window late.
        reconciler.RecordResult(Account, At(0.99), "Savage", 100);
        Assert.False(Sample(reconciler, 100, 1));
        Assert.False(Sample(reconciler, 200, 2)); // silent, but it closes that pair
        // The next fight's XP shows before the poll and its result just after it.
        Assert.False(Sample(reconciler, 300, 3));
        reconciler.RecordResult(Account, At(3.01), "Savage", 100);
        Assert.False(Sample(reconciler, 300, 4));
    }

    [Fact]
    public void AMissBeforeASkippedWindowNeverPairsWithALaterOne()
    {
        var reconciler = new XpReconciler();
        Sample(reconciler, 100, 0);
        Assert.False(Sample(reconciler, 200, 1)); // one miss: a fight whose result lands just after this poll

        // That fight levelled up, so the next window is skipped and the run of misses starts over.
        var levelled = new PlayerProgressSnapshot("cody", "Savage",
            new Dictionary<string, ClassProfileSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                ["Savage"] = new(13, 50, Cap(13), null),
                ["Mage"] = new(3, 0, Cap(3), null)
            },
            []);
        reconciler.RecordResult(Account, At(1.1), "Savage", Cap(12) - 200 + 50);
        Assert.False(reconciler.RecordSample(Account, levelled, At(2), At(-1)));

        Assert.False(reconciler.RecordSample(Account, levelled with
        {
            Classes = new Dictionary<string, ClassProfileSnapshot>(levelled.Classes, StringComparer.OrdinalIgnoreCase)
            {
                ["Savage"] = new(13, 150, Cap(13), null)
            }
        }, At(3), At(-1)));
    }

    [Fact]
    public void ResultsUnderAnotherSpellingOfTheClassAreNotMisses()
    {
        // The game and the website spell the class differently, so no result matches it, but results do arrive.
        var reconciler = new XpReconciler();
        Sample(reconciler, 100, 0);
        reconciler.RecordResult(Account, At(0.5), "Savages", 100);
        Assert.False(Sample(reconciler, 200, 1));
        reconciler.RecordResult(Account, At(1.5), "Savages", 100);
        Assert.False(Sample(reconciler, 300, 2));
    }
}
