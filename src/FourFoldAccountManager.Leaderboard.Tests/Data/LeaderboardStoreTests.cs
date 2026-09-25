using FourFoldAccountManager.Core.Leaderboard;
using FourFoldAccountManager.Leaderboard.Service.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace FourFoldAccountManager.Leaderboard.Tests.Data;

public sealed class LeaderboardStoreTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase($"leaderboard_{Guid.NewGuid():N}")
        .Build();
    private LeaderboardDbContext _db = null!;
    private EfLeaderboardStore _store = null!;
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var options = new DbContextOptionsBuilder<LeaderboardDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options;
        _db = new LeaderboardDbContext(options);
        await _db.Database.MigrateAsync();
        _store = new EfLeaderboardStore(_db);
    }

    public async Task DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task HeartbeatReplacesOnlyItsInstallationAndActiveSet()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await _store.ApplyHeartbeatAsync(Heartbeat(first, (1, "Alice"), (2, "Bob")), Now, default);
        await _store.ApplyHeartbeatAsync(Heartbeat(second, (3, "Carol")), Now.AddMinutes(2), default);
        await _store.ApplyHeartbeatAsync(new ParticipationHeartbeat(first, true,
            [new LeaderboardProfile(2, "Bob New")], [2]), Now.AddMinutes(1), default);

        var active = await _store.GetActiveProfilesAsync(Now, default);
        Assert.Equal([2, 3], active.Select(x => x.PlayerId).ToArray());
        Assert.Equal("Bob New", active[0].Username);
        Assert.Equal(2, await _db.InstallationProfiles.CountAsync());
    }

    [Fact]
    public async Task OptOutClearsOnlyThatInstallationAndRetainsRecordedGains()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await _store.ApplyHeartbeatAsync(Heartbeat(first, (1, "Alice")), Now, default);
        await _store.ApplyHeartbeatAsync(Heartbeat(second, (2, "Bob")), Now, default);
        await SeedObservationAsync(Observation(1, "Alice", 25, Now));
        await _store.ApplyHeartbeatAsync(new ParticipationHeartbeat(first, false, [], []), Now.AddMinutes(1), default);

        var activeIds = (await _store.GetActiveProfilesAsync(Now.AddMilliseconds(-1), default)).Select(x => x.PlayerId).ToArray();
        Assert.Equal([2], activeIds);
        Assert.NotNull(await _store.GetPlayerStateAsync(1, default));
        Assert.Equal(25, Assert.Single((await _store.GetPageAsync(LeaderboardPeriod.Daily, 1, 50, Now,
            TimeSpan.FromMinutes(3), default)).Entries).XpGained);
    }

    [Fact]
    public async Task LongSamplingIntervalStillRebaselinesAfterLeaseExpiresAndReactivates()
    {
        var installation = Guid.NewGuid();
        await _store.ApplyHeartbeatAsync(Heartbeat(installation, (1, "Alice")), Now, default);
        await SeedObservationAsync(Observation(1, "Alice", null, Now));

        // A collector interval longer than the three-minute lease has not run in between.
        await _store.ApplyHeartbeatAsync(Heartbeat(installation, (1, "Alice")), Now.AddMinutes(5), default);

        Assert.True((await _store.GetPlayerStateAsync(1, default))!.NeedsBaseline);
    }

    [Fact]
    public async Task DeactivationAndReactivationRebaselineEvenWithinSampleInterval()
    {
        var installation = Guid.NewGuid();
        await _store.ApplyHeartbeatAsync(Heartbeat(installation, (1, "Alice")), Now, default);
        await SeedObservationAsync(Observation(1, "Alice", null, Now));
        await _store.ApplyHeartbeatAsync(new ParticipationHeartbeat(installation, true,
            [new LeaderboardProfile(1, "Alice")], []), Now.AddMinutes(1), default);

        Assert.True((await _store.GetPlayerStateAsync(1, default))!.NeedsBaseline);
        await _store.ApplyHeartbeatAsync(Heartbeat(installation, (1, "Alice")), Now.AddMinutes(2), default);
        Assert.True((await _store.GetPlayerStateAsync(1, default))!.NeedsBaseline);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task RejectsNonpositivePlayerIds(int badId)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _store.ApplyHeartbeatAsync(
            new ParticipationHeartbeat(Guid.NewGuid(), true, [new LeaderboardProfile(badId, "Invalid")], []), Now, default));
    }

    [Fact]
    public async Task RejectsDuplicateAndUnlinkedActiveIds()
    {
        var id = Guid.NewGuid();
        await Assert.ThrowsAsync<ArgumentException>(() => _store.ApplyHeartbeatAsync(
            new ParticipationHeartbeat(id, true, [new(1, "A"), new(1, "A")], [1]), Now, default));
        await Assert.ThrowsAsync<ArgumentException>(() => _store.ApplyHeartbeatAsync(
            new ParticipationHeartbeat(id, true, [new(1, "A")], [2]), Now, default));
        await Assert.ThrowsAsync<ArgumentException>(() => _store.ApplyHeartbeatAsync(
            new ParticipationHeartbeat(id, true, [new(1, "A")], [1, 1]), Now, default));
    }

    [Fact]
    public async Task ActiveLeaseExcludesExactCutoff()
    {
        await _store.ApplyHeartbeatAsync(Heartbeat(Guid.NewGuid(), (1, "Alice")), Now, default);
        Assert.Empty(await _store.GetActiveProfilesAsync(Now, default));
        Assert.Single(await _store.GetActiveProfilesAsync(Now.AddMilliseconds(-1), default));
    }

    [Fact]
    public async Task PendingBaselineQueryTracksActivationAndEstablishedBaseline()
    {
        var installation = Guid.NewGuid();
        await _store.ApplyHeartbeatAsync(Heartbeat(installation, (1, "Alice")), Now, default);
        var pending = await _store.GetActiveProfilesNeedingBaselineAsync(Now.AddMilliseconds(-1), default);
        Assert.Equal([1], pending.Select(x => x.PlayerId).ToArray());

        await SeedObservationAsync(Observation(1, "Alice", null, Now));
        await _store.ApplyHeartbeatAsync(Heartbeat(installation, (1, "Alice")), Now.AddMinutes(1), default);
        Assert.Empty(await _store.GetActiveProfilesNeedingBaselineAsync(Now, default));

        await _store.ApplyHeartbeatAsync(new ParticipationHeartbeat(installation, true,
            [new LeaderboardProfile(1, "Alice")], []), Now.AddMinutes(2), default);
        Assert.Empty(await _store.GetActiveProfilesNeedingBaselineAsync(Now, default));
        await _store.ApplyHeartbeatAsync(Heartbeat(installation, (1, "Alice")), Now.AddMinutes(3), default);
        pending = await _store.GetActiveProfilesNeedingBaselineAsync(Now.AddMinutes(2), default);
        Assert.Equal([1], pending.Select(x => x.PlayerId).ToArray());
    }

    [Fact]
    public async Task ConcurrentHeartbeatsKeepOneCompleteProfileSet()
    {
        var installationId = Guid.NewGuid();
        await _store.ApplyHeartbeatAsync(Heartbeat(installationId, (1, "Initial")), Now, default);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(async batch =>
        {
            var options = new DbContextOptionsBuilder<LeaderboardDbContext>()
                .UseNpgsql(_postgres.GetConnectionString()).Options;
            await using var context = new LeaderboardDbContext(options);
            var profiles = Enumerable.Range(0, 3)
                .Select(index => (Id: 100 + batch * 10 + index, Username: $"Batch {batch}"))
                .ToArray();
            await new EfLeaderboardStore(context).ApplyHeartbeatAsync(
                Heartbeat(installationId, profiles), Now.AddMinutes(batch + 1), default);
        }));

        var finalRows = await _db.InstallationProfiles.AsNoTracking()
            .Where(x => x.InstallationId == installationId)
            .OrderBy(x => x.PlayerId)
            .ToArrayAsync();
        Assert.Equal(3, finalRows.Length);
        Assert.Equal(3, finalRows.Select(x => x.PlayerId).Distinct().Count());
        var winningBatch = (finalRows[0].PlayerId - 100) / 10;
        Assert.Equal(Enumerable.Range(0, 3).Select(index => 100 + winningBatch * 10 + index),
            finalRows.Select(x => x.PlayerId));
        Assert.All(finalRows, row => Assert.Equal($"Batch {winningBatch}", row.Username));
    }

    [Fact]
    public async Task AggregatesByPlayerAndSortsDeterministicallyWithPageBounds()
    {
        await SeedObservationAsync(Observation(3, "zed", 10, Now));
        await SeedObservationAsync(Observation(2, "alice", 10, Now));
        await SeedObservationAsync(Observation(1, "Alice", 10, Now));
        await SeedObservationAsync(Observation(3, "zed", 5, Now.AddMinutes(1)));

        var first = await _store.GetPageAsync(LeaderboardPeriod.Daily, 1, 2, Now.AddMinutes(1),
            TimeSpan.FromMinutes(3), default);
        var second = await _store.GetPageAsync(LeaderboardPeriod.Daily, 2, 2, Now.AddMinutes(1),
            TimeSpan.FromMinutes(3), default);
        Assert.Equal(3, first.TotalEntries);
        Assert.Equal([3, 1], first.Entries.Select(x => x.PlayerId).ToArray());
        Assert.Equal([1, 2], first.Entries.Select(x => x.Rank).ToArray());
        Assert.Equal(15, first.Entries[0].XpGained);
        Assert.Equal([2], second.Entries.Select(x => x.PlayerId).ToArray());
        Assert.Equal(3, second.Entries[0].Rank);
        Assert.Empty((await _store.GetPageAsync(LeaderboardPeriod.Daily, 9, 2, Now,
            TimeSpan.FromMinutes(3), default)).Entries);
        Assert.Equal(100, (await _store.GetPageAsync(LeaderboardPeriod.Daily, 1, 200, Now,
            TimeSpan.FromMinutes(3), default)).PageSize);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _store.GetPageAsync(
            LeaderboardPeriod.Daily, 0, 10, Now, TimeSpan.FromMinutes(3), default));
    }

    [Fact]
    public async Task UsesHalfOpenUtcWindows()
    {
        var window = LeaderboardPeriodWindow.GetCurrent(LeaderboardPeriod.Daily, Now);
        await SeedObservationAsync(Observation(1, "Alice", 2, window.StartUtc));
        await SeedObservationAsync(Observation(1, "Alice", 3, window.EndUtc.AddMicroseconds(-1)));
        await SeedObservationAsync(Observation(1, "Alice", 7, window.EndUtc));

        var page = await _store.GetPageAsync(LeaderboardPeriod.Daily, 1, 50, Now,
            TimeSpan.FromMinutes(3), default);
        Assert.Equal(5, Assert.Single(page.Entries).XpGained);
    }

    [Fact]
    public async Task PrunesOldEventsButKeepsLatestPlayerState()
    {
        await SeedObservationAsync(Observation(1, "Alice", 2, Now.AddDays(-41)));
        await SeedObservationAsync(Observation(1, "Alice", 3, Now.AddDays(-39)));
        await _store.DeleteGainEventsBeforeAsync(Now.AddDays(-40), default);

        Assert.Single(await _db.XpGainEvents.ToListAsync());
        Assert.NotNull(await _store.GetPlayerStateAsync(1, default));
    }

    [Fact]
    public async Task FailedFetchMarksBaselineWithoutReplacingLastGoodSnapshot()
    {
        await SeedObservationAsync(Observation(1, "Alice", null, Now));
        var previous = await _store.GetPlayerStateAsync(1, default);
        await _store.MarkNeedsBaselineAsync(1, default);
        var state = await _store.GetPlayerStateAsync(1, default);
        Assert.NotNull(state);
        Assert.True(state.NeedsBaseline);
        Assert.Equal(previous!.SnapshotJson, state.SnapshotJson);
        Assert.Equal(Now, state.LastSampledAtUtc);
    }

    [Fact]
    public async Task ObservationAfterOptOutPreservesBaselineAndWritesNoGain()
    {
        var installation = Guid.NewGuid();
        await _store.ApplyHeartbeatAsync(Heartbeat(installation, (1, "Alice")), Now, default);
        await SeedObservationAsync(Observation(1, "Alice", null, Now));
        var expected = await _store.GetPlayerStateAsync(1, default);
        await _store.ApplyHeartbeatAsync(new ParticipationHeartbeat(installation, false, [], []),
            Now.AddMinutes(1), default);

        await _store.SaveObservationAsync(Observation(1, "Alice", 25, Now.AddMinutes(1)),
            expected, TimeSpan.FromMinutes(3), default);

        Assert.True((await _store.GetPlayerStateAsync(1, default))!.NeedsBaseline);
        Assert.Empty(await _db.XpGainEvents.ToListAsync());
    }

    [Fact]
    public async Task ObservationAfterLeaseExpiryPreservesBaselineAndWritesNoGain()
    {
        await _store.ApplyHeartbeatAsync(Heartbeat(Guid.NewGuid(), (1, "Alice")), Now, default);
        await SeedObservationAsync(Observation(1, "Alice", null, Now));
        var expected = await _store.GetPlayerStateAsync(1, default);

        await _store.SaveObservationAsync(Observation(1, "Alice", 25, Now.AddMinutes(3)),
            expected, TimeSpan.FromMinutes(3), default);

        Assert.True((await _store.GetPlayerStateAsync(1, default))!.NeedsBaseline);
        Assert.Empty(await _db.XpGainEvents.ToListAsync());
    }

    [Fact]
    public async Task ChangedSampleStateCannotBeOverwrittenByInflightObservation()
    {
        await _store.ApplyHeartbeatAsync(Heartbeat(Guid.NewGuid(), (1, "Alice")), Now, default);
        await SeedObservationAsync(Observation(1, "Alice", null, Now));
        var expected = await _store.GetPlayerStateAsync(1, default);
        await _store.MarkNeedsBaselineAsync(1, default);

        await _store.SaveObservationAsync(Observation(1, "Alice", 25, Now.AddMinutes(1)),
            expected, TimeSpan.FromMinutes(3), default);

        Assert.True((await _store.GetPlayerStateAsync(1, default))!.NeedsBaseline);
        Assert.Empty(await _db.XpGainEvents.ToListAsync());
    }

    [Fact]
    public async Task AnotherFreshInstallationKeepsPlayerEligibleAfterFirstOptsOut()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await _store.ApplyHeartbeatAsync(Heartbeat(first, (1, "Alice")), Now, default);
        await _store.ApplyHeartbeatAsync(Heartbeat(second, (1, "Alice")), Now, default);
        await SeedObservationAsync(Observation(1, "Alice", null, Now));
        var expected = await _store.GetPlayerStateAsync(1, default);
        await _store.ApplyHeartbeatAsync(new ParticipationHeartbeat(first, false, [], []),
            Now.AddMinutes(1), default);

        await _store.SaveObservationAsync(Observation(1, "Alice", 25, Now.AddMinutes(1)),
            expected, TimeSpan.FromMinutes(3), default);

        Assert.False((await _store.GetPlayerStateAsync(1, default))!.NeedsBaseline);
        Assert.Equal(25, Assert.Single(await _db.XpGainEvents.ToListAsync()).Gain);
    }

    [Fact]
    public async Task ServiceCeilingsRejectNewEnrollmentAndKeepExistingUpdatesAndOptOut()
    {
        var store = new EfLeaderboardStore(_db, new LeaderboardCapacityOptions
        {
            MaxInstallations = 1,
            MaxProfileLinks = 1
        });
        var installation = Guid.NewGuid();
        await store.ApplyHeartbeatAsync(Heartbeat(installation, (1, "Alice")), Now, default);
        await Assert.ThrowsAsync<LeaderboardCapacityExceededException>(() =>
            store.ApplyHeartbeatAsync(Heartbeat(Guid.NewGuid(), (2, "Bob")), Now, default));
        await Assert.ThrowsAsync<LeaderboardCapacityExceededException>(() =>
            store.ApplyHeartbeatAsync(Heartbeat(installation, (1, "Alice"), (2, "Bob")), Now, default));

        await store.ApplyHeartbeatAsync(Heartbeat(installation, (1, "Alice Updated")),
            Now.AddMinutes(1), default);
        Assert.Equal("Alice Updated", Assert.Single(await _db.InstallationProfiles.ToListAsync()).Username);
        await store.ApplyHeartbeatAsync(new ParticipationHeartbeat(installation, false, [], []),
            Now.AddMinutes(2), default);
        Assert.Empty(await _db.InstallationProfiles.ToListAsync());
    }

    [Fact]
    public async Task CleanupExpiresStaleInstallationsAndTheirLinks()
    {
        var stale = Guid.NewGuid();
        var fresh = Guid.NewGuid();
        await _store.ApplyHeartbeatAsync(Heartbeat(stale, (1, "Alice")), Now.AddDays(-41), default);
        await _store.ApplyHeartbeatAsync(Heartbeat(fresh, (2, "Bob")), Now.AddDays(-39), default);

        await _store.DeleteInactiveInstallationsBeforeAsync(Now.AddDays(-40), default);

        Assert.Equal(fresh, Assert.Single(await _db.Installations.AsNoTracking().ToListAsync()).InstallationId);
        Assert.Equal(2, Assert.Single(await _db.InstallationProfiles.AsNoTracking().ToListAsync()).PlayerId);
    }

    [Fact]
    public async Task PendingBaselineCountIsAnonymousDistinctAndSurvivesEventPruning()
    {
        await _store.ApplyHeartbeatAsync(Heartbeat(Guid.NewGuid(), (1, "Alice"), (2, "Bob")), Now, default);
        await _store.ApplyHeartbeatAsync(Heartbeat(Guid.NewGuid(), (1, "Alice")), Now, default);
        var empty = await _store.GetPageAsync(LeaderboardPeriod.Daily, 1, 25, Now,
            TimeSpan.FromMinutes(3), default);
        Assert.Equal(2, empty.PendingBaselineProfiles);
        Assert.Empty(empty.Entries);
        await SeedObservationAsync(Observation(1, "Alice", 3, Now));
        var ranked = await _store.GetPageAsync(LeaderboardPeriod.Daily, 1, 25, Now,
            TimeSpan.FromMinutes(3), default);
        Assert.Equal(1, ranked.PendingBaselineProfiles);
        Assert.Equal(1, Assert.Single(ranked.Entries).PlayerId);
        await _store.DeleteGainEventsBeforeAsync(Now.AddMinutes(1), default);

        var afterPrune = await _store.GetPageAsync(LeaderboardPeriod.Daily, 1, 25, Now.AddMinutes(1),
            TimeSpan.FromMinutes(3), default);
        Assert.Equal(1, afterPrune.PendingBaselineProfiles);
        Assert.Empty(afterPrune.Entries);
    }

    private async Task SeedObservationAsync(PlayerObservation observation)
    {
        var state = await _db.PlayerSampleStates.FindAsync(observation.PlayerId);
        if (state is null)
        {
            state = new PlayerSampleStateEntity { PlayerId = observation.PlayerId };
            _db.PlayerSampleStates.Add(state);
        }
        state.Username = observation.Username;
        state.SnapshotJson = observation.SnapshotJson;
        state.LastSampledAtUtc = observation.ObservedAtUtc;
        state.NeedsBaseline = observation.NeedsBaseline;
        if (observation.ValidGain is > 0)
        {
            state.HasEverScoredGain = true;
            _db.XpGainEvents.Add(new XpGainEventEntity
            {
                PlayerId = observation.PlayerId,
                Username = observation.Username,
                Gain = observation.ValidGain.Value,
                ObservedAtUtc = observation.ObservedAtUtc
            });
        }
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private static ParticipationHeartbeat Heartbeat(Guid installationId, params (int Id, string Username)[] profiles) =>
        new(installationId, true, profiles.Select(p => new LeaderboardProfile(p.Id, p.Username)).ToArray(),
            profiles.Select(p => p.Id).ToArray());

    private static PlayerObservation Observation(int id, string name, long? gain, DateTimeOffset time) =>
        new(id, name, "{\"classes\":{}}", gain, time, false);
}
