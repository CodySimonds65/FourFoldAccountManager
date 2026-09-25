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
    public async Task ChangedSampleStateDuringFetchCannotScoreOrClearBaseline()
    {
        var store = Seed(10);
        var source = new CallbackSource(Snapshot("Alice", 35), () =>
            store.MarkNeedsBaselineAsync(1, default).GetAwaiter().GetResult());

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
        await service.RunOnceAsync(Now.AddMinutes(2), default);

        Assert.Empty(store.Gains);
        Assert.False(store.States[1].NeedsBaseline);
        Assert.InRange(store.States[1].LastSampledAtUtc, Now.AddMinutes(2), Now.AddMinutes(2).AddSeconds(1));
    }

    [Fact]
    public async Task ExpiredActivityLeaseIsExcluded()
    {
        var store = new FakeStore();
        store.Activate(1, "Alice", Now.AddMinutes(-3));
        var source = new FakeSource(Snapshot("Alice", 10));

        await Create(store, source).RunOnceAsync(Now, default);

        Assert.Equal(0, source.FetchCount);
        Assert.Empty(store.States);
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
    public async Task RecoveredInvalidClassIsBaselinedWhileOtherClassContinuesScoring()
    {
        var store = Seed(10, 10);
        var source = new FakeSource(
            Snapshot("Alice", 5, 20),
            Snapshot("Alice", 15, 25));
        var service = Create(store, source);

        await service.RunOnceAsync(Now.AddMinutes(1), default);
        await service.RunOnceAsync(Now.AddMinutes(2).AddSeconds(1), default);

        Assert.Equal([10L, 5L], store.Gains);
        Assert.Empty(XpSnapshotJson.Deserialize(store.States[1].SnapshotJson, "Alice").InvalidClasses);
    }

    [Fact]
    public async Task AllInvalidClassDataRequestsRebaseline()
    {
        var store = Seed(10);
        var source = new FakeSource(Snapshot("Alice", 5));

        await Create(store, source).RunOnceAsync(Now.AddMinutes(1), default);

        Assert.Empty(store.Gains);
        Assert.True(store.States[1].NeedsBaseline);
    }

    [Fact]
    public async Task MalformedSnapshotWithNoValidClassesKeepsLastGoodState()
    {
        var store = Seed(10);
        var malformed = new PlayerProgressSnapshot("Alice", null,
            new Dictionary<string, ClassProfileSnapshot>(), ["Warrior"]);
        var source = new FakeSource(malformed);

        await Create(store, source).RunOnceAsync(Now.AddMinutes(1), default);

        Assert.True(store.States[1].NeedsBaseline);
        Assert.Equal(Now, store.States[1].LastSampledAtUtc);
        Assert.Empty(store.Gains);
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
    public async Task DisabledCollectionNeverCallsSource()
    {
        var store = Seed(10);
        var source = new FakeSource(Snapshot("Alice", 30));
        var service = new LeaderboardSamplingService(store, source,
            new LeaderboardCollectionOptions { Enabled = false, MinimumSampleInterval = TimeSpan.FromMinutes(1) });

        await service.RunOnceAsync(Now.AddMinutes(1), default);

        Assert.Equal(0, source.FetchCount);
    }

    [Fact]
    public async Task SpacesRequestsForDifferentPlayersByApprovedInterval()
    {
        var store = new FakeStore();
        store.Activate(1, "Alice", Now);
        store.Activate(2, "Bob", Now);
        var source = new FakeSource(Snapshot("Alice", 5), Snapshot("Bob", 5));
        var service = new LeaderboardSamplingService(store, source,
            new LeaderboardCollectionOptions { Enabled = true, MinimumSampleInterval = TimeSpan.FromMilliseconds(80) });
        var timer = Stopwatch.StartNew();

        await service.RunOnceAsync(Now, default);

        Assert.Equal(2, source.FetchCount);
        Assert.True(timer.Elapsed >= TimeSpan.FromMilliseconds(65), $"Elapsed: {timer.Elapsed}");
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
            new LeaderboardCollectionOptions { Enabled = true, MinimumSampleInterval = TimeSpan.FromMilliseconds(20) });

        await service.RunOnceAsync(Now, default);

        Assert.Equal([9, 1, 2, 3], source.PlayerIds);
        Assert.Null(Assert.Single(store.Observations, x => x.PlayerId == 9).ValidGain);
        Assert.Equal(25, XpSnapshotJson.Deserialize(store.States[9].SnapshotJson, "Alice")
            .Classes["Warrior"].CurrentXp);
    }

    [Fact]
    public async Task ActivationDuringPassGetsBaselineBeforeRemainingRoutineProfiles()
    {
        var store = new FakeStore();
        foreach (var id in new[] { 1, 2, 3 }) store.Activate(id, "Alice", Now);
        foreach (var id in new[] { 1, 2, 3 })
            store.States[id] = new PlayerSampleState(id, "Alice",
                XpSnapshotJson.Serialize(Snapshot("Alice", 10)), Now.AddMilliseconds(-40), false);
        var source = new RecordingSource(id =>
        {
            if (id == 1) store.Activate(9, "Alice", Now);
            return Snapshot("Alice", 35);
        });
        var service = new LeaderboardSamplingService(store, source,
            new LeaderboardCollectionOptions { Enabled = true, MinimumSampleInterval = TimeSpan.FromMilliseconds(20) });

        await service.RunOnceAsync(Now, default);

        Assert.Equal([1, 9, 2, 3], source.PlayerIds);
        Assert.Null(Assert.Single(store.Observations, x => x.PlayerId == 9).ValidGain);
        Assert.False(store.States[9].NeedsBaseline);
    }

    [Fact]
    public async Task ActivationDuringRequestWaitGetsNextAvailableSlot()
    {
        var store = new FakeStore();
        foreach (var id in new[] { 1, 2 }) store.Activate(id, "Alice", Now);
        foreach (var id in new[] { 1, 2 })
            store.States[id] = new PlayerSampleState(id, "Alice",
                XpSnapshotJson.Serialize(Snapshot("Alice", 10)), Now.AddMinutes(-1), false);
        var source = new RecordingSource(_ => Snapshot("Alice", 35));
        var clock = new ActivationOnDelayTimeProvider(() => store.Activate(9, "Alice", Now));
        var service = new LeaderboardSamplingService(store, source,
            new LeaderboardCollectionOptions { Enabled = true, MinimumSampleInterval = TimeSpan.FromMilliseconds(20) },
            clock);

        await service.RunOnceAsync(Now, default);

        Assert.Equal([1, 9, 2], source.PlayerIds);
        Assert.Null(Assert.Single(store.Observations, x => x.PlayerId == 9).ValidGain);
    }

    [Fact]
    public async Task PriorityBaselineKeepsApprovedRequestSpacing()
    {
        var store = new FakeStore();
        store.Activate(1, "Alice", Now);
        store.Activate(9, "Alice", Now);
        store.States[1] = new PlayerSampleState(1, "Alice",
            XpSnapshotJson.Serialize(Snapshot("Alice", 10)), Now.AddMinutes(-1), false);
        var source = new RecordingSource(_ => Snapshot("Alice", 35));
        var service = new LeaderboardSamplingService(store, source,
            new LeaderboardCollectionOptions { Enabled = true, MinimumSampleInterval = TimeSpan.FromMilliseconds(80) });

        await service.RunOnceAsync(Now, default);

        Assert.Equal([9, 1], source.PlayerIds);
        Assert.True(Stopwatch.GetElapsedTime(source.Timestamps[0], source.Timestamps[1]) >=
            TimeSpan.FromMilliseconds(65));
    }

    [Fact]
    public async Task ThreeContinuouslyActivePlayersDoNotLoseQueuedGain()
    {
        var store = new FakeStore();
        foreach (var id in new[] { 1, 2, 3 }) store.Activate(id, "Alice", Now);
        var before = Snapshot("Alice", 10) with { ActiveClassName = "Warrior" };
        var after = Snapshot("Alice", 35) with { ActiveClassName = "Warrior" };
        var local = new XpTrackingSession();
        local.ApplySnapshot(before, Now);

        var service = new LeaderboardSamplingService(store, new PerPlayerSource(1, 2, 3),
            new LeaderboardCollectionOptions
            {
                Enabled = true,
                MinimumSampleInterval = TimeSpan.FromMilliseconds(20)
            });

        var elapsed = Stopwatch.StartNew();
        await service.RunOnceAsync(Now, default);
        elapsed.Stop();
        var nextAt = Now + elapsed.Elapsed + TimeSpan.FromMilliseconds(20);
        local.ApplySnapshot(after, nextAt);
        Assert.Equal(25, local.SessionGain);
        await service.RunOnceAsync(nextAt, default);

        Assert.Equal(new long[] { 25, 25, 25 }, store.Gains.Order().ToArray());
    }

    [Fact]
    public async Task ShrinkingQueueDoesNotDiscardContinuouslyActivePlayersGain()
    {
        var store = new FakeStore();
        foreach (var id in Enumerable.Range(1, 5)) store.Activate(id, "Alice", Now);
        var interval = TimeSpan.FromMilliseconds(20);
        var schedule = new LeaderboardSamplingSchedule();
        var source = new PerPlayerSource(1, 2, 3, 4, 5);
        var options = new LeaderboardCollectionOptions { Enabled = true, MinimumSampleInterval = interval };
        var service = new LeaderboardSamplingService(store,
            source, options, null, schedule);
        var elapsed = Stopwatch.StartNew();

        await service.RunOnceAsync(Now, default);
        elapsed.Stop();
        store.Deactivate(4);
        store.Deactivate(5);
        await new LeaderboardSamplingService(store, source, options, null, schedule)
            .RunOnceAsync(Now + elapsed.Elapsed + interval, default);

        Assert.Equal(new long[] { 25, 25, 25 }, store.Gains.Order().ToArray());
    }

    [Fact]
    public async Task SuccessfulFetchLatencyDoesNotDiscardNextPassGain()
    {
        var store = new FakeStore();
        foreach (var id in Enumerable.Range(1, 5)) store.Activate(id, "Alice", Now);
        var interval = TimeSpan.FromMilliseconds(20);
        var service = new LeaderboardSamplingService(store,
            new PerPlayerSource(TimeSpan.FromMilliseconds(8), 1, 2, 3, 4, 5),
            new LeaderboardCollectionOptions { Enabled = true, MinimumSampleInterval = interval });
        var elapsed = Stopwatch.StartNew();

        await service.RunOnceAsync(Now, default);
        elapsed.Stop();
        await service.RunOnceAsync(Now + elapsed.Elapsed + interval, default);

        Assert.Equal(new long[] { 25, 25, 25, 25, 25 }, store.Gains.Order().ToArray());
    }

    [Fact]
    public async Task IdleWorkerRebaselinesAfterUncertainGap()
    {
        var store = new FakeStore();
        foreach (var id in new[] { 1, 2, 3 }) store.Activate(id, "Alice", Now);
        var interval = TimeSpan.FromMilliseconds(20);
        var service = new LeaderboardSamplingService(store, new PerPlayerSource(1, 2, 3),
            new LeaderboardCollectionOptions { Enabled = true, MinimumSampleInterval = interval });
        var elapsed = Stopwatch.StartNew();

        await service.RunOnceAsync(Now, default);
        elapsed.Stop();
        await service.RunOnceAsync(Now + elapsed.Elapsed + interval * 4, default);

        Assert.Empty(store.Gains);
        Assert.All(store.States.Values, state =>
            Assert.Equal(25, XpSnapshotJson.Deserialize(state.SnapshotJson, state.Username)
                .Classes["Warrior"].CurrentXp));
    }

    [Fact]
    public async Task GapBeyondQueueAllowanceStillCreatesFreshBaseline()
    {
        var store = Seed(10);
        store.Activate(2, "Alice", Now);
        store.Activate(3, "Alice", Now);
        var source = new FakeSource(Snapshot("Alice", 35), Snapshot("Alice", 10),
            Snapshot("Alice", 10));
        var service = new LeaderboardSamplingService(store, source,
            new LeaderboardCollectionOptions
            {
                Enabled = true,
                MinimumSampleInterval = TimeSpan.FromMilliseconds(20)
            });

        await service.RunOnceAsync(Now.AddMilliseconds(100), default);

        Assert.Empty(store.Gains);
        Assert.False(store.States[1].NeedsBaseline);
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

    private sealed class PerPlayerSource : ILeaderboardPublicProfileSource
    {
        private readonly Dictionary<int, Queue<PlayerProgressSnapshot>> _responses;
        private readonly TimeSpan _delay;

        public PerPlayerSource(params int[] playerIds) : this(TimeSpan.Zero, playerIds) { }

        public PerPlayerSource(TimeSpan delay, params int[] playerIds)
        {
            _delay = delay;
            _responses = playerIds.ToDictionary(id => id, _ => new Queue<PlayerProgressSnapshot>(
                [Snapshot("Alice", 10) with { ActiveClassName = "Warrior" },
                 Snapshot("Alice", 35) with { ActiveClassName = "Warrior" }]));
        }

        public async Task<PlayerProgressSnapshot> FetchAsync(int playerId, CancellationToken ct)
        {
            if (_delay > TimeSpan.Zero) await Task.Delay(_delay, ct);
            return _responses[playerId].Dequeue();
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

    private sealed class ActivationOnDelayTimeProvider(Action activate) : TimeProvider
    {
        private int _activated;
        public override long TimestampFrequency => TimeProvider.System.TimestampFrequency;
        public override long GetTimestamp() => TimeProvider.System.GetTimestamp();
        public override ITimer CreateTimer(TimerCallback callback, object? state,
            TimeSpan dueTime, TimeSpan period)
        {
            if (Interlocked.Exchange(ref _activated, 1) == 0) activate();
            return TimeProvider.System.CreateTimer(callback, state, dueTime, period);
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
        public async Task<IReadOnlyList<ActiveLeaderboardProfile>> GetActiveProfilesNeedingBaselineAsync(
            DateTimeOffset activeAfterUtc, CancellationToken ct) =>
            (await GetActiveProfilesAsync(activeAfterUtc, ct)).Where(profile =>
                !States.TryGetValue(profile.PlayerId, out var state) || state.NeedsBaseline).ToArray();
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
