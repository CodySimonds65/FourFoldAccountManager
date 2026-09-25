using FourFoldAccountManager.Core.Leaderboard;

namespace FourFoldAccountManager.Leaderboard.Service.Data;

public sealed record ActiveLeaderboardProfile(int PlayerId, string Username);

public sealed record PlayerSampleState(
    int PlayerId,
    string Username,
    string SnapshotJson,
    DateTimeOffset LastSampledAtUtc,
    bool NeedsBaseline);

public sealed record PlayerObservation(
    int PlayerId,
    string Username,
    string SnapshotJson,
    long? ValidGain,
    DateTimeOffset ObservedAtUtc,
    bool NeedsBaseline);

public interface ILeaderboardStore
{
    Task ApplyHeartbeatAsync(ParticipationHeartbeat heartbeat, DateTimeOffset receivedAtUtc, CancellationToken ct);
    Task<IReadOnlyList<ActiveLeaderboardProfile>> GetActiveProfilesAsync(DateTimeOffset activeAfterUtc, CancellationToken ct);
    // Active profiles awaiting a baseline first, then those last sampled at or before sampledBeforeUtc, oldest first.
    Task<IReadOnlyList<ActiveLeaderboardProfile>> GetProfilesDueForSampleAsync(
        DateTimeOffset activeAfterUtc, DateTimeOffset sampledBeforeUtc, CancellationToken ct);
    Task<PlayerSampleState?> GetPlayerStateAsync(int playerId, CancellationToken ct);
    Task SaveObservationAsync(PlayerObservation observation, PlayerSampleState? expectedState,
        TimeSpan activeLeaseDuration, CancellationToken ct);
    Task MarkNeedsBaselineAsync(int playerId, CancellationToken ct);
    Task<LeaderboardPage> GetPageAsync(LeaderboardPeriod period, int page, int pageSize,
        DateTimeOffset nowUtc, TimeSpan staleAfter, CancellationToken ct);
    Task DeleteGainEventsBeforeAsync(DateTimeOffset cutoffUtc, CancellationToken ct);
    Task DeleteInactiveInstallationsBeforeAsync(DateTimeOffset cutoffUtc, CancellationToken ct);
}
