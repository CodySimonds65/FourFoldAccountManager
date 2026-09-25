using FourFoldAccountManager.Core.Leaderboard;
using FourFoldAccountManager.Leaderboard.Service.Collection;
using FourFoldAccountManager.Leaderboard.Service.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FourFoldAccountManager.Leaderboard.Tests.Collection;

public sealed class LeaderboardRetentionWorkerTests
{
    [Fact]
    public async Task DisabledCollectionStillPrunesScoreEventsOlderThanConfiguredRetention()
    {
        var store = new RecordingStore();
        using var provider = new ServiceCollection()
            .AddSingleton<ILeaderboardStore>(store)
            .BuildServiceProvider();
        var options = new LeaderboardCollectionOptions { Enabled = false, RetentionDays = 40 };
        var before = DateTimeOffset.UtcNow.AddDays(-40);
        using var worker = new LeaderboardRetentionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(), options, TimeProvider.System);

        await worker.StartAsync(default);
        var cutoff = await store.Cutoff.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(default);

        Assert.InRange(cutoff, before, DateTimeOffset.UtcNow.AddDays(-40));
    }

    [Fact]
    public async Task DisabledCollectionStillExpiresInactiveInstallations()
    {
        var store = new RecordingStore();
        using var provider = new ServiceCollection()
            .AddSingleton<ILeaderboardStore>(store)
            .BuildServiceProvider();
        var before = DateTimeOffset.UtcNow.AddDays(-40);
        using var worker = new LeaderboardRetentionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new LeaderboardCollectionOptions { Enabled = false, RetentionDays = 40 },
            TimeProvider.System);

        await worker.StartAsync(default);
        var cutoff = await store.InstallationCutoff.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(default);

        Assert.InRange(cutoff, before, DateTimeOffset.UtcNow.AddDays(-40));
    }

    private sealed class RecordingStore : ILeaderboardStore
    {
        public TaskCompletionSource<DateTimeOffset> Cutoff { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<DateTimeOffset> InstallationCutoff { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task DeleteGainEventsBeforeAsync(DateTimeOffset cutoffUtc, CancellationToken ct)
        {
            Cutoff.TrySetResult(cutoffUtc);
            return Task.CompletedTask;
        }

        public Task ApplyHeartbeatAsync(ParticipationHeartbeat heartbeat, DateTimeOffset receivedAtUtc, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<ActiveLeaderboardProfile>> GetActiveProfilesAsync(DateTimeOffset activeAfterUtc, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<ActiveLeaderboardProfile>> GetActiveProfilesNeedingBaselineAsync(
            DateTimeOffset activeAfterUtc, CancellationToken ct) => throw new NotImplementedException();
        public Task<PlayerSampleState?> GetPlayerStateAsync(int playerId, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task SaveObservationAsync(PlayerObservation observation, PlayerSampleState? expectedState,
            TimeSpan activeLeaseDuration, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task MarkNeedsBaselineAsync(int playerId, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<LeaderboardPage> GetPageAsync(LeaderboardPeriod period, int page, int pageSize,
            DateTimeOffset nowUtc, TimeSpan staleAfter, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task DeleteInactiveInstallationsBeforeAsync(DateTimeOffset cutoffUtc, CancellationToken ct)
        {
            InstallationCutoff.TrySetResult(cutoffUtc);
            return Task.CompletedTask;
        }
    }
}
