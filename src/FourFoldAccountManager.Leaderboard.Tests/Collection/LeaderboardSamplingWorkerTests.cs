using FourFoldAccountManager.Core.Leaderboard;
using FourFoldAccountManager.Core.Tracking;
using FourFoldAccountManager.Leaderboard.Service.Collection;
using FourFoldAccountManager.Leaderboard.Service.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FourFoldAccountManager.Leaderboard.Tests.Collection;

public sealed class LeaderboardSamplingWorkerTests
{
    [Theory]
    [InlineData(false, 60000)]
    [InlineData(true, 0)]
    public async Task UnsafeCollectionConfigurationNeverCallsSource(bool enabled, int intervalMilliseconds)
    {
        var source = new CountingSource();
        var options = new LeaderboardCollectionOptions
        {
            Enabled = enabled,
            MinimumSampleInterval = TimeSpan.FromMilliseconds(intervalMilliseconds)
        };
        using var provider = new ServiceCollection()
            .AddSingleton<ILeaderboardStore, ActiveStore>()
            .AddSingleton<ILeaderboardPublicProfileSource>(source)
            .AddSingleton(options)
            .AddTransient<LeaderboardSamplingService>()
            .BuildServiceProvider();
        var worker = new LeaderboardSamplingWorker(provider.GetRequiredService<IServiceScopeFactory>(),
            options, TimeProvider.System);

        await worker.StartAsync(default);
        await worker.StopAsync(default);

        Assert.Equal(0, source.Calls);
    }

    private sealed class CountingSource : ILeaderboardPublicProfileSource
    {
        public int Calls { get; private set; }
        public Task<PlayerProgressSnapshot> FetchAsync(int playerId, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new PlayerProgressSnapshot("Alice", null,
                new Dictionary<string, ClassProfileSnapshot> { ["Warrior"] = new(1, 5, 10, null) }, []));
        }
    }

    private sealed class ActiveStore : ILeaderboardStore
    {
        public Task<IReadOnlyList<ActiveLeaderboardProfile>> GetActiveProfilesAsync(DateTimeOffset activeAfterUtc, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ActiveLeaderboardProfile>>([new(1, "Alice")]);
        public Task<PlayerSampleState?> GetPlayerStateAsync(int playerId, CancellationToken ct) => Task.FromResult<PlayerSampleState?>(null);
        public Task SaveObservationAsync(PlayerObservation observation, CancellationToken ct) => Task.CompletedTask;
        public Task MarkNeedsBaselineAsync(int playerId, CancellationToken ct) => Task.CompletedTask;
        public Task ApplyHeartbeatAsync(ParticipationHeartbeat heartbeat, DateTimeOffset receivedAtUtc, CancellationToken ct) => throw new NotImplementedException();
        public Task<LeaderboardPage> GetPageAsync(LeaderboardPeriod period, int page, int pageSize, DateTimeOffset nowUtc, TimeSpan staleAfter, CancellationToken ct) => throw new NotImplementedException();
        public Task DeleteGainEventsBeforeAsync(DateTimeOffset cutoffUtc, CancellationToken ct) => throw new NotImplementedException();
    }
}
