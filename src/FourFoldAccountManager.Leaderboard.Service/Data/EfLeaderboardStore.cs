using System.Buffers.Binary;
using System.Security.Cryptography;
using FourFoldAccountManager.Core.Leaderboard;
using Microsoft.EntityFrameworkCore;

namespace FourFoldAccountManager.Leaderboard.Service.Data;

public sealed class EfLeaderboardStore(LeaderboardDbContext db, LeaderboardCapacityOptions? capacity = null) : ILeaderboardStore
{
    private readonly LeaderboardCapacityOptions _capacity = capacity ?? new LeaderboardCapacityOptions();
    private const int PlayerLockNamespace = 20260923;
    private const long EnrollmentLockKey = 2026092301;

    private async Task LockPlayerAsync(int playerId, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({PlayerLockNamespace}, {playerId})", ct);

    public async Task ApplyHeartbeatAsync(ParticipationHeartbeat heartbeat, DateTimeOffset receivedAtUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(heartbeat);
        if (heartbeat.InstallationId == Guid.Empty) throw new ArgumentException("Installation ID is required.", nameof(heartbeat));
        if (heartbeat.LinkedProfiles is null || heartbeat.ActivePlayerIds is null)
            throw new ArgumentException("Profile lists are required.", nameof(heartbeat));

        var linked = new HashSet<int>();
        foreach (var profile in heartbeat.LinkedProfiles)
        {
            if (profile is null || profile.PlayerId <= 0 || !linked.Add(profile.PlayerId) ||
                string.IsNullOrWhiteSpace(profile.Username) || profile.Username.Length > 256)
                throw new ArgumentException("Linked profiles must have unique positive IDs and valid public usernames.", nameof(heartbeat));
        }

        var active = new HashSet<int>();
        foreach (var id in heartbeat.ActivePlayerIds)
        {
            if (!active.Add(id) || !linked.Contains(id))
                throw new ArgumentException("Active IDs must be unique and linked.", nameof(heartbeat));
        }

        var now = receivedAtUtc.ToUniversalTime();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Global enrollment lock makes both capacity counts and cleanup atomic across instances.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({EnrollmentLockKey})", ct);
        // Serialize replacements for this installation across service instances, including its first insert.
        var lockKey = BinaryPrimitives.ReadInt64BigEndian(
            SHA256.HashData(heartbeat.InstallationId.ToByteArray()));
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", ct);
        var installation = await db.Installations.FindAsync([heartbeat.InstallationId], ct);
        if (installation is null)
        {
            if (!heartbeat.SharingEnabled)
            {
                await transaction.CommitAsync(ct);
                return;
            }
            if (await db.Installations.CountAsync(ct) >= _capacity.MaxInstallations)
                throw new LeaderboardCapacityExceededException("The leaderboard installation limit has been reached.");
            installation = new InstallationParticipationEntity { InstallationId = heartbeat.InstallationId };
            db.Installations.Add(installation);
        }
        installation.SharingEnabled = heartbeat.SharingEnabled;
        installation.LastHeartbeatAtUtc = now;

        var previous = await db.InstallationProfiles
            .Where(x => x.InstallationId == heartbeat.InstallationId).ToListAsync(ct);
        var incomingLinks = heartbeat.SharingEnabled ? heartbeat.LinkedProfiles.Count : 0;
        if (incomingLinks > previous.Count &&
            await db.InstallationProfiles.CountAsync(ct) + incomingLinks - previous.Count > _capacity.MaxProfileLinks)
            throw new LeaderboardCapacityExceededException("The leaderboard profile-link limit has been reached.");
        var affectedIds = previous.Where(x => x.IsActive).Select(x => x.PlayerId)
            .Concat(active).Distinct().ToArray();
        foreach (var playerId in affectedIds.OrderBy(x => x))
            await LockPlayerAsync(playerId, ct);
        var activeCutoff = now - TimeSpan.FromMinutes(3);
        var freshBefore = affectedIds.Length == 0 ? [] : await db.InstallationProfiles.AsNoTracking()
            .Where(x => affectedIds.Contains(x.PlayerId) && x.Installation.SharingEnabled &&
                x.IsActive && x.LastActiveAtUtc > activeCutoff)
            .Select(x => x.PlayerId).Distinct().ToArrayAsync(ct);
        db.InstallationProfiles.RemoveRange(previous);
        await db.SaveChangesAsync(ct);

        if (heartbeat.SharingEnabled)
        {
            db.InstallationProfiles.AddRange(heartbeat.LinkedProfiles.Select(profile => new InstallationProfileEntity
            {
                InstallationId = heartbeat.InstallationId,
                PlayerId = profile.PlayerId,
                Username = profile.Username.Trim(),
                IsActive = active.Contains(profile.PlayerId),
                LastActiveAtUtc = active.Contains(profile.PlayerId) ? now : null
            }));
            await db.SaveChangesAsync(ct);
        }
        if (affectedIds.Length > 0)
        {
            var freshAfter = await db.InstallationProfiles.AsNoTracking()
                .Where(x => affectedIds.Contains(x.PlayerId) && x.Installation.SharingEnabled &&
                    x.IsActive && x.LastActiveAtUtc > activeCutoff)
                .Select(x => x.PlayerId).Distinct().ToArrayAsync(ct);
            var beforeSet = freshBefore.ToHashSet();
            var afterSet = freshAfter.ToHashSet();
            var rebaselineIds = affectedIds.Where(id =>
                (active.Contains(id) && !beforeSet.Contains(id)) ||
                (!afterSet.Contains(id) && beforeSet.Contains(id))).ToArray();
            if (rebaselineIds.Length > 0)
            {
                await db.PlayerSampleStates.Where(x => rebaselineIds.Contains(x.PlayerId))
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.NeedsBaseline, true), ct);
            }
        }
        await transaction.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<ActiveLeaderboardProfile>> GetActiveProfilesAsync(
        DateTimeOffset activeAfterUtc, CancellationToken ct)
    {
        var cutoff = activeAfterUtc.ToUniversalTime();
        var active = await db.InstallationProfiles.AsNoTracking()
            .Where(x => x.Installation.SharingEnabled && x.IsActive && x.LastActiveAtUtc > cutoff)
            .Select(x => new { x.PlayerId, x.Username, x.LastActiveAtUtc })
            .ToListAsync(ct);
        return active.GroupBy(x => x.PlayerId)
            .Select(group => group.OrderByDescending(x => x.LastActiveAtUtc)
                .ThenBy(x => x.Username, StringComparer.OrdinalIgnoreCase).First())
            .OrderBy(x => x.PlayerId)
            .Select(x => new ActiveLeaderboardProfile(x.PlayerId, x.Username))
            .ToArray();
    }

    public async Task<PlayerSampleState?> GetPlayerStateAsync(int playerId, CancellationToken ct)
    {
        if (playerId <= 0) throw new ArgumentOutOfRangeException(nameof(playerId));
        var state = await db.PlayerSampleStates.AsNoTracking()
            .SingleOrDefaultAsync(x => x.PlayerId == playerId, ct);
        return state is null ? null : new PlayerSampleState(state.PlayerId, state.Username,
            state.SnapshotJson, state.LastSampledAtUtc, state.NeedsBaseline);
    }

    public async Task SaveObservationAsync(PlayerObservation observation, PlayerSampleState? expectedState,
        TimeSpan activeLeaseDuration, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.PlayerId <= 0) throw new ArgumentOutOfRangeException(nameof(observation));
        if (string.IsNullOrWhiteSpace(observation.Username) || observation.Username.Length > 256)
            throw new ArgumentException("Public username is required.", nameof(observation));
        if (observation.ValidGain < 0) throw new ArgumentOutOfRangeException(nameof(observation));
        if (activeLeaseDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(activeLeaseDuration));

        var minimalSnapshotJson = XpSnapshotJson.Normalize(observation.SnapshotJson);
        var observedAt = observation.ObservedAtUtc.ToUniversalTime();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockPlayerAsync(observation.PlayerId, ct);
        var active = await db.InstallationProfiles.AsNoTracking().AnyAsync(x =>
            x.PlayerId == observation.PlayerId && x.Installation.SharingEnabled &&
            x.IsActive && x.LastActiveAtUtc > observedAt - activeLeaseDuration, ct);
        var state = await db.PlayerSampleStates.AsNoTracking()
            .SingleOrDefaultAsync(x => x.PlayerId == observation.PlayerId, ct);
        var unchanged = state is null
            ? expectedState is null
            : expectedState is not null &&
              state.Username == expectedState.Username &&
              state.SnapshotJson == expectedState.SnapshotJson &&
              state.LastSampledAtUtc == expectedState.LastSampledAtUtc &&
              state.NeedsBaseline == expectedState.NeedsBaseline;
        if (!active || !unchanged)
        {
            if (state is not null)
                await db.PlayerSampleStates.Where(x => x.PlayerId == observation.PlayerId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.NeedsBaseline, true), ct);
            await transaction.CommitAsync(ct);
            return;
        }
        if (state is null)
        {
            state = new PlayerSampleStateEntity
            {
                PlayerId = observation.PlayerId,
                Username = observation.Username.Trim(),
                SnapshotJson = minimalSnapshotJson,
                LastSampledAtUtc = observedAt,
                NeedsBaseline = observation.NeedsBaseline,
                HasEverScoredGain = observation.ValidGain is > 0
            };
            db.PlayerSampleStates.Add(state);
        }
        else
            await db.PlayerSampleStates.Where(x => x.PlayerId == observation.PlayerId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Username, observation.Username.Trim())
                    .SetProperty(x => x.SnapshotJson, minimalSnapshotJson)
                    .SetProperty(x => x.LastSampledAtUtc, observedAt)
                    .SetProperty(x => x.NeedsBaseline, observation.NeedsBaseline)
                    .SetProperty(x => x.HasEverScoredGain,
                        state.HasEverScoredGain || observation.ValidGain > 0), ct);

        if (observation.ValidGain is > 0)
        {
            db.XpGainEvents.Add(new XpGainEventEntity
            {
                PlayerId = observation.PlayerId,
                Username = observation.Username.Trim(),
                Gain = observation.ValidGain.Value,
                ObservedAtUtc = observedAt
            });
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task MarkNeedsBaselineAsync(int playerId, CancellationToken ct)
    {
        if (playerId <= 0) throw new ArgumentOutOfRangeException(nameof(playerId));
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockPlayerAsync(playerId, ct);
        await db.PlayerSampleStates.Where(x => x.PlayerId == playerId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.NeedsBaseline, true), ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<LeaderboardPage> GetPageAsync(LeaderboardPeriod period, int page, int pageSize,
        DateTimeOffset nowUtc, TimeSpan staleAfter, CancellationToken ct)
    {
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page));
        if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (staleAfter < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(staleAfter));
        pageSize = Math.Min(pageSize, 100);
        var now = nowUtc.ToUniversalTime();
        var (start, end) = LeaderboardPeriodWindow.GetCurrent(period, now);

        var totals = db.XpGainEvents.AsNoTracking()
            .Where(x => x.ObservedAtUtc >= start && x.ObservedAtUtc < end)
            .GroupBy(x => x.PlayerId)
            .Select(g => new { PlayerId = g.Key, XpGained = g.Sum(x => x.Gain) });
        var ranked = from total in totals
                     join state in db.PlayerSampleStates.AsNoTracking()
                         on total.PlayerId equals state.PlayerId
                     select new
                     {
                         total.PlayerId,
                         total.XpGained,
                         state.Username,
                         state.LastSampledAtUtc,
                         state.NeedsBaseline
                     };
        var totalEntries = await ranked.CountAsync(ct);
        var skip = Math.Min((long)(page - 1) * pageSize, int.MaxValue);
        var rows = await ranked.OrderByDescending(x => x.XpGained)
            .ThenBy(x => x.Username.ToLower())
            .ThenBy(x => x.PlayerId)
            .Skip((int)skip).Take(pageSize).ToArrayAsync(ct);
        var entries = rows.Select((x, index) =>
            new LeaderboardEntry((int)skip + index + 1, x.PlayerId, x.Username,
                x.XpGained, x.LastSampledAtUtc,
                x.NeedsBaseline || now - x.LastSampledAtUtc > staleAfter)).ToArray();
        var pendingBaselineProfiles = await db.InstallationProfiles.AsNoTracking()
            .Where(x => x.Installation.SharingEnabled &&
                !db.PlayerSampleStates.Any(state =>
                    state.PlayerId == x.PlayerId && state.HasEverScoredGain))
            .Select(x => x.PlayerId).Distinct().CountAsync(ct);
        return new LeaderboardPage(period, start, end, page, pageSize, totalEntries, now, entries,
            pendingBaselineProfiles);
    }

    public Task DeleteGainEventsBeforeAsync(DateTimeOffset cutoffUtc, CancellationToken ct) =>
        db.XpGainEvents.Where(x => x.ObservedAtUtc < cutoffUtc.ToUniversalTime()).ExecuteDeleteAsync(ct);

    public async Task DeleteInactiveInstallationsBeforeAsync(DateTimeOffset cutoffUtc, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({EnrollmentLockKey})", ct);
        await db.Installations.Where(x => x.LastHeartbeatAtUtc < cutoffUtc.ToUniversalTime())
            .ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
