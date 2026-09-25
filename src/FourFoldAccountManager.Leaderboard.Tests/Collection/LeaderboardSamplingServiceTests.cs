using FourFoldAccountManager.Core.Leaderboard;
using FourFoldAccountManager.Core.Tracking;
using FourFoldAccountManager.Leaderboard.Service.Collection;
using FourFoldAccountManager.Leaderboard.Service.Data;
using System.Diagnostics;
using Xunit;

namespace FourFoldAccountManager.Leaderboard.Tests.Collection;

public sealed class LeaderboardSamplingServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FirstSampleEstablishesZeroGainBaselineThenNextSampleScores()
    {
        var store = new FakeStore();
        store.Activate(1, "Alice", Now);
        var source = new FakeSource(Snapshot("Alice", 10), Snapshot("ALICE", 35));
        var service = Create(store, source);

        await service.RunOnceAsync(Now, default);
        Assert.Empty(store.Gains);
        Assert.InRange(store.States[1].LastSampledAtUtc, Now, Now.AddSeconds(1));
        await service.RunOnceAsync(Now.AddMinutes(1).AddSeconds(1), default);

        Assert.Equal([25L], store.Gains);
        Assert.Equal(2, source.FetchCount);
    }

    [Fact]
    public async Task OptOutDuringFetchCannotScoreOrClearBaseline()
    {
        var store = Seed(10);
        var source = new CallbackSource(Snapshot("Alice", 35), () =>
        {
            store.Deactivate(1);
            store.MarkNeedsBaselineAsync(1, default).GetAwaiter().GetResult();
        });

        await Create(store, source).RunOnceAsync(Now.AddMinutes(1), default);

        Assert.Empty(store.Gains);
        Assert.True(store.States[1].NeedsBaseline);
    }

    [Fact]
    public async Task SamplesEachPlayerIdOnceAcrossInstallations()
    {
        var store = new FakeStore();
        store.Activate(1, "Alice", Now);
        store.Activate(1, "Alice", Now);
        var source = new FakeSource(Snapshot("Alice", 10));

        await Create(store, source).RunOnceAsync(Now, default);

        Assert.Equal(1, source.FetchCount);
        Assert.Single(store.States);
    }

    [Fact]
    public async Task UsernameMismatchRequestsRebaselineWithoutScore()
    {
        var store = Seed(10);
        var source = new FakeSource(Snapshot("Mallory", 30));

        await Create(store, source).RunOnceAsync(Now.AddMinutes(1), default);

        Assert.True(store.States[1].NeedsBaseline);
        Assert.Empty(store.Gains);
        Assert.Equal(Now, store.States[1].LastSampledAtUtc);
    }

    [Fact]
    public async Task SourceFailureRequestsRebaselineAndNextSuccessScoresNothing()
    {
        var store = Seed(10);
        var source = new FakeSource(new InvalidDataException("bad page"), Snapshot("Alice", 30));
        var service = Create(store, source);

        await service.RunOnceAsync(Now.AddMinutes(1), default);
        Assert.True(store.States[1].NeedsBaseline);
        await service.RunOnceAsync(Now.AddMinutes(2).AddSeconds(1), default);

        Assert.Empty(store.Gains);
        Assert.False(store.States[1].NeedsBaseline);
        Assert.InRange(store.States[1].LastSampledAtUtc, Now.AddMinutes(2).AddSeconds(1), Now.AddMinutes(2).AddSeconds(2));
    }

    [Fact]
    public async Task RebaselinesAfterUncertainGapWithoutWritingGain()
    {
        var store = Seed(10);
        store.Activate(1, "Alice", Now.AddMinutes(4));
        var source = new FakeSource(Snapshot("Alice", 50));

        await Create(store, source).RunOnceAsync(Now.AddMinutes(4), default);

        Assert.Empty(store.Gains);
        Assert.InRange(store.States[1].LastSampledAtUtc, Now.AddMinutes(4), Now.AddMinutes(4).AddSeconds(1));
        Assert.False(store.States[1].NeedsBaseline);
    }

    [Fact]
    public async Task InvalidClassDeltaDoesNotReduceOrInventGain()
    {
        var store = Seed(10, 10);
        var invalid = new PlayerProgressSnapshot("Alice", null,
            new Dictionary<string, ClassProfileSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                ["Warrior"] = Class(5),
                ["Mage"] = Class(35)
            }, []);
        var source = new FakeSource(invalid);

        await Create(store, source).RunOnceAsync(Now.AddMinutes(1), default);

        Assert.Equal([25L], store.Gains);
    }

    [Fact]
    public async Task FirstMalformedSnapshotDoesNotBecomeBaseline()
    {
        var store = new FakeStore();
        store.Activate(1, "Alice", Now);
        var malformed = new PlayerProgressSnapshot("Alice", null,
            new Dictionary<string, ClassProfileSnapshot>(), ["Warrior"]);

        await Create(store, new FakeSource(malformed)).RunOnceAsync(Now, default);

        Assert.Empty(store.States);
        Assert.Equal(1, store.BaselineMarks);
    }

    [Fact]
    public async Task NewParticipantGetsBaselineBeforeRoutineQueue()
    {
        var store = new FakeStore();
        foreach (var id in new[] { 1, 2, 3, 9 }) store.Activate(id, "Alice", Now);
        foreach (var id in new[] { 1, 2, 3 })
            store.States[id] = new PlayerSampleState(id, "Alice",
                XpSnapshotJson.Serialize(Snapshot("Alice", 10)), Now.AddMilliseconds(-40), false);
        var source = new RecordingSource(_ => Snapshot("Alice", 35));
        var service = new LeaderboardSamplingService(store, source,
            Options(TimeSpan.FromMilliseconds(20)));

        await service.RunOnceAsync(Now, default);

        Assert.Equal([9, 1, 2, 3], source.PlayerIds);
        Assert.Null(Assert.Single(store.Observations, x => x.PlayerId == 9).ValidGain);
        Assert.Equal(25, XpSnapshotJson.Deserialize(store.States[9].SnapshotJson, "Alice")
            .Classes["Warrior"].CurrentXp);
    }

    [Fact]
    public async Task LeaderboardCountsAllClassesWhileLocalSessionCountsActiveClass()
    {
        var before = Snapshot("Alice", 10, 10) with { ActiveClassName = "Warrior" };
        var after = Snapshot("Alice", 35, 20) with { ActiveClassName = "Warrior" };
        var local = new XpTrackingSession();
        local.ApplySnapshot(before, Now);
        local.ApplySnapshot(after, Now.AddMinutes(1));

        var store = Seed(10, 10);
        await Create(store, new FakeSource(after)).RunOnceAsync(Now.AddMinutes(1), default);

        Assert.Equal(25, local.SessionGain);
        Assert.Equal(35, Assert.Single(store.Gains));
    }

    [Fact]
    public async Task AttributesGainWhenSourceResponseWasObservedAcrossUtcBoundary()
    {
        var beforeMidnight = new DateTimeOffset(2026, 9, 23, 23, 59, 59, 990, TimeSpan.Zero);
        var store = new FakeStore();
        store.Activate(1, "Alice", beforeMidnight);
        store.States[1] = new PlayerSampleState(1, "Alice", XpSnapshotJson.Serialize(Snapshot("Alice", 5)),
            beforeMidnight.AddMinutes(-1), false);
        var source = new DelayedSource(Snapshot("Alice", 10), TimeSpan.FromMilliseconds(40));
        var service = new LeaderboardSamplingService(store, source, new LeaderboardCollectionOptions
        {
            Enabled = true,
            MinimumSampleInterval = TimeSpan.FromMinutes(1)
        });

        await service.RunOnceAsync(beforeMidnight, default);

        Assert.Equal([5L], store.Gains);
        Assert.True(store.States[1].LastSampledAtUtc >= new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero));
    }

    private static LeaderboardSamplingService Create(FakeStore store, ILeaderboardPublicProfileSource source) =>
        new(store, source, new LeaderboardCollectionOptions
        {
            Enabled = true,
            MinimumSampleInterval = TimeSpan.FromMinutes(1)
        });

    private static LeaderboardCollectionOptions Options(TimeSpan interval, TimeSpan? spacing = null) =>
        new() { Enabled = true, MinimumSampleInterval = interval, RequestSpacing = spacing ?? TimeSpan.Zero };

    private static FakeStore Seed(long warriorXp, long? mageXp = null)
    {
        var store = new FakeStore();
        store.Activate(1, "Alice", Now);
        store.States[1] = new PlayerSampleState(1, "Alice",
            XpSnapshotJson.Serialize(Snapshot("Alice", warriorXp, mageXp)), Now, false);
        return store;
    }

    private static PlayerProgressSnapshot Snapshot(string username, long warriorXp, long? mageXp = null)
    {
        var classes = new Dictionary<string, ClassProfileSnapshot>(StringComparer.OrdinalIgnoreCase)
        {
            ["Warrior"] = Class(warriorXp)
        };
        if (mageXp is { } xp) classes["Mage"] = Class(xp);
        return new PlayerProgressSnapshot(username, null, classes, []);
    }

    private static ClassProfileSnapshot Class(long xp) => xp switch
    {
        < 10 => new(1, xp, 10, null),
        < 40 => new(2, xp - 10, 30, null),
        _ => new(3, xp - 40, 60, null)
    };

    private sealed class FakeSource(params object[] responses) : ILeaderboardPublicProfileSource
    {
        public int FetchCount { get; private set; }
        public Task<PlayerProgressSnapshot> FetchAsync(int playerId, CancellationToken ct)
        {
            var response = responses[FetchCount++];
            return response is Exception ex ? Task.FromException<PlayerProgressSnapshot>(ex) :
                Task.FromResult((PlayerProgressSnapshot)response);
        }
    }

    private sealed class DelayedSource(PlayerProgressSnapshot response, TimeSpan delay) : ILeaderboardPublicProfileSource
    {
        public async Task<PlayerProgressSnapshot> FetchAsync(int playerId, CancellationToken ct)
        {
            await Task.Delay(delay, ct);
            return response;
        }
    }

    private sealed class RecordingSource(Func<int, PlayerProgressSnapshot> response) : ILeaderboardPublicProfileSource
    {
        public List<int> PlayerIds { get; } = [];
        public List<long> Timestamps { get; } = [];
        public Task<PlayerProgressSnapshot> FetchAsync(int playerId, CancellationToken ct)
        {
            PlayerIds.Add(playerId);
            Timestamps.Add(Stopwatch.GetTimestamp());
            return Task.FromResult(response(playerId));
        }
    }

    private sealed class CallbackSource(PlayerProgressSnapshot response, Action callback) : ILeaderboardPublicProfileSource
    {
        public Task<PlayerProgressSnapshot> FetchAsync(int playerId, CancellationToken ct)
        {
            callback();
            return Task.FromResult(response);
        }
    }

    private sealed class FakeStore : ILeaderboardStore
    {
        private readonly List<(ActiveLeaderboardProfile Profile, DateTimeOffset At)> _active = [];
        public Dictionary<int, PlayerSampleState> States { get; } = [];
        public List<long> Gains { get; } = [];
        public List<PlayerObservation> Observations { get; } = [];
        public int BaselineMarks { get; private set; }

        public void Activate(int id, string username, DateTimeOffset at) =>
            _active.Add((new ActiveLeaderboardProfile(id, username), at));
        public void Deactivate(int id) => _active.RemoveAll(x => x.Profile.PlayerId == id);
        public Task<IReadOnlyList<ActiveLeaderboardProfile>> GetActiveProfilesAsync(DateTimeOffset activeAfterUtc, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ActiveLeaderboardProfile>>(_active.Where(x => x.At > activeAfterUtc)
                .Select(x => x.Profile).ToArray());
        public async Task<IReadOnlyList<ActiveLeaderboardProfile>> GetProfilesDueForSampleAsync(
            DateTimeOffset activeAfterUtc, DateTimeOffset sampledBeforeUtc, CancellationToken ct) =>
            (await GetActiveProfilesAsync(activeAfterUtc, ct)).DistinctBy(profile => profile.PlayerId)
                .Select(profile => (Profile: profile, State: States.GetValueOrDefault(profile.PlayerId)))
                .Where(x => x.State is null || x.State.NeedsBaseline || x.State.LastSampledAtUtc <= sampledBeforeUtc)
                .OrderByDescending(x => x.State is null || x.State.NeedsBaseline)
                .ThenBy(x => x.State?.LastSampledAtUtc).ThenBy(x => x.Profile.PlayerId)
                .Select(x => x.Profile).ToArray();
        public Task<PlayerSampleState?> GetPlayerStateAsync(int playerId, CancellationToken ct) =>
            Task.FromResult(States.GetValueOrDefault(playerId));
        public Task SaveObservationAsync(PlayerObservation observation, PlayerSampleState? expectedState,
            TimeSpan activeLeaseDuration, CancellationToken ct)
        {
            var active = _active.Any(x => x.Profile.PlayerId == observation.PlayerId &&
                x.At > observation.ObservedAtUtc - activeLeaseDuration);
            var currentState = States.GetValueOrDefault(observation.PlayerId);
            if (!active || currentState != expectedState)
            {
                if (currentState is not null)
                    States[observation.PlayerId] = currentState with { NeedsBaseline = true };
                return Task.CompletedTask;
            }
            States[observation.PlayerId] = new PlayerSampleState(observation.PlayerId, observation.Username,
                observation.SnapshotJson, observation.ObservedAtUtc, observation.NeedsBaseline);
            Observations.Add(observation);
            if (observation.ValidGain is { } gain) Gains.Add(gain);
            return Task.CompletedTask;
        }
        public Task MarkNeedsBaselineAsync(int playerId, CancellationToken ct)
        {
            BaselineMarks++;
            if (States.TryGetValue(playerId, out var state)) States[playerId] = state with { NeedsBaseline = true };
            return Task.CompletedTask;
        }
        public Task ApplyHeartbeatAsync(ParticipationHeartbeat heartbeat, DateTimeOffset receivedAtUtc, CancellationToken ct) => throw new NotImplementedException();
        public Task<LeaderboardPage> GetPageAsync(LeaderboardPeriod period, int page, int pageSize, DateTimeOffset nowUtc, TimeSpan staleAfter, CancellationToken ct) => throw new NotImplementedException();
        public Task DeleteGainEventsBeforeAsync(DateTimeOffset cutoffUtc, CancellationToken ct) => throw new NotImplementedException();
        public Task DeleteInactiveInstallationsBeforeAsync(DateTimeOffset cutoffUtc, CancellationToken ct) => throw new NotImplementedException();
    }
}
