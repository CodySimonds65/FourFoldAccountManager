using System.IO;
using System.Net.Http;
using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Tracking;
using FourFoldAccountManager.Desktop.Services;
using Xunit;

namespace FourFoldAccountManager.Desktop.Tests.Services;

public sealed class XpTrackerSnapshotTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"fourfold-tracker-{Guid.NewGuid():N}");

    public XpTrackerSnapshotTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task LatestSnapshotAppearsOnlyAfterASuccessfulFetch()
    {
        var snapshot = new PlayerProgressSnapshot("Player", "Warrior", new Dictionary<string, ClassProfileSnapshot>
        {
            ["Warrior"] = new(2, 5, 30, null) { ClassName = "Warrior" }
        }, []);
        await using var tracker = new XpTrackerCoordinator(new LocalDataPaths(_root), _ => Task.CompletedTask,
            new PlayerProfileService(new FixedTransport(snapshot)));
        var accountId = Guid.NewGuid();
        tracker.Start(accountId, "Player", 42);

        Assert.Null(tracker.GetLatestSnapshot(accountId));
        await tracker.PollOnceAsync(CancellationToken.None);

        Assert.Same(snapshot, tracker.GetLatestSnapshot(accountId));
        Assert.Null(tracker.GetLatestSnapshot(Guid.NewGuid()));
    }

    [Fact]
    public async Task AFailedReadAfterARenameDoesNotPairTheOldPlayerIdWithTheNewUsername()
    {
        var transport = new FixedTransport(Snapshot("Login"), new RankingEntry("Login", 7));
        await using var tracker = new XpTrackerCoordinator(new LocalDataPaths(_root), _ => Task.CompletedTask,
            new PlayerProfileService(transport)) { ProfileRetryDelay = TimeSpan.Zero };
        var accountId = Guid.NewGuid();
        tracker.Start(accountId, "Login", null);
        Poll(tracker);
        Assert.Equal(7, Assert.Single(tracker.GetActiveLeaderboardProfiles()).PlayerId);

        transport.Offline = true;
        tracker.Start(accountId, "Ranked", null);
        Poll(tracker);

        Assert.Empty(tracker.GetActiveLeaderboardProfiles());
    }

    [Fact]
    public async Task ALinkedPlayerIsStillTrackedAfterTheFirstReadFails()
    {
        // The ranking is empty, so only the linked id can find this player.
        var snapshot = Snapshot("Player");
        var transport = new FixedTransport(snapshot) { Offline = true };
        await using var tracker = new XpTrackerCoordinator(new LocalDataPaths(_root), _ => Task.CompletedTask,
            new PlayerProfileService(transport)) { ProfileRetryDelay = TimeSpan.Zero };
        var accountId = Guid.NewGuid();
        tracker.Start(accountId, "Player", 42);
        await tracker.PollOnceAsync(CancellationToken.None);

        transport.Offline = false;
        await tracker.PollOnceAsync(CancellationToken.None);

        Assert.Same(snapshot, tracker.GetLatestSnapshot(accountId));
    }

    private static PlayerProgressSnapshot Snapshot(string username) =>
        new(username, "Warrior", new Dictionary<string, ClassProfileSnapshot>
        {
            ["Warrior"] = new(2, 5, 30, null) { ClassName = "Warrior" }
        }, []);

    // Start and GetActiveLeaderboardProfiles must run on the thread that created the tracker, and an awaited
    // poll may resume on another one, so this blocks on the poll instead.
    private static void Poll(XpTrackerCoordinator tracker) =>
        Task.Run(() => tracker.PollOnceAsync(CancellationToken.None)).GetAwaiter().GetResult();

    private sealed class FixedTransport(PlayerProgressSnapshot snapshot, params RankingEntry[] ranking)
        : IPlayerProfileTransport
    {
        public bool Offline { get; set; }

        public Task<IReadOnlyList<RankingEntry>> GetRankingAsync(CancellationToken cancellationToken) =>
            Offline ? throw new HttpRequestException() : Task.FromResult<IReadOnlyList<RankingEntry>>(ranking);

        public Task<PlayerProgressSnapshot> GetProfileAsync(int playerId, CancellationToken cancellationToken) =>
            Offline ? throw new HttpRequestException() : Task.FromResult(snapshot);
    }
}
