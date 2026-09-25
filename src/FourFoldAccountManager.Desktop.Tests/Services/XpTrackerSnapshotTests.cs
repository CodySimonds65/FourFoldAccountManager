using System.IO;
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

    private sealed class FixedTransport(PlayerProgressSnapshot snapshot) : IPlayerProfileTransport
    {
        public Task<IReadOnlyList<RankingEntry>> GetRankingAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RankingEntry>>([]);

        public Task<PlayerProgressSnapshot> GetProfileAsync(int playerId, CancellationToken cancellationToken) =>
            Task.FromResult(snapshot);
    }
}
