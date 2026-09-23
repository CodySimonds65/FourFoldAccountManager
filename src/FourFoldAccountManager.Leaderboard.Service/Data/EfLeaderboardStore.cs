using System.Buffers.Binary;
using System.Security.Cryptography;
using FourFoldAccountManager.Core.Leaderboard;
using Microsoft.EntityFrameworkCore;

namespace FourFoldAccountManager.Leaderboard.Service.Data;

public sealed class EfLeaderboardStore(LeaderboardDbContext db) : ILeaderboardStore
{
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
        // Serialize replacements for this installation across service instances, including its first insert.
        var lockKey = BinaryPrimitives.ReadInt64BigEndian(
            SHA256.HashData(heartbeat.InstallationId.ToByteArray()));
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", ct);
        var installation = await db.Installations.FindAsync([heartbeat.InstallationId], ct);
        if (installation is null)
        {
            installation = new InstallationParticipationEntity { InstallationId = heartbeat.InstallationId };
            db.Installations.Add(installation);
        }
        installation.SharingEnabled = heartbeat.SharingEnabled;
        installation.LastHeartbeatAtUtc = now;

        var previous = await db.InstallationProfiles
            .Where(x => x.InstallationId == heartbeat.InstallationId).ToListAsync(ct);
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

    public async Task SaveObservationAsync(PlayerObservation observation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.PlayerId <= 0) throw new ArgumentOutOfRangeException(nameof(observation));
        if (string.IsNullOrWhiteSpace(observation.Username) || observation.Username.Length > 256)
            throw new ArgumentException("Public username is required.", nameof(observation));
        if (observation.ValidGain < 0) throw new ArgumentOutOfRangeException(nameof(observation));

        var minimalSnapshotJson = XpSnapshotJson.Normalize(observation.SnapshotJson);
        var observedAt = observation.ObservedAtUtc.ToUniversalTime();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var state = await db.PlayerSampleStates.FindAsync([observation.PlayerId], ct);
        if (state is null)
        {
            state = new PlayerSampleStateEntity { PlayerId = observation.PlayerId };
            db.PlayerSampleStates.Add(state);
        }
        state.Username = observation.Username.Trim();
        state.SnapshotJson = minimalSnapshotJson;
        state.LastSampledAtUtc = observedAt;
        state.NeedsBaseline = observation.NeedsBaseline;

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
        await db.PlayerSampleStates.Where(x => x.PlayerId == playerId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.NeedsBaseline, true), ct);
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

        var totals = await db.XpGainEvents.AsNoTracking()
            .Where(x => x.ObservedAtUtc >= start && x.ObservedAtUtc < end)
            .GroupBy(x => x.PlayerId)
            .Select(g => new { PlayerId = g.Key, XpGained = g.Sum(x => x.Gain) })
            .ToListAsync(ct);
        var ids = totals.Select(x => x.PlayerId).ToArray();
        var states = await db.PlayerSampleStates.AsNoTracking()
            .Where(x => ids.Contains(x.PlayerId))
            .ToDictionaryAsync(x => x.PlayerId, ct);
        var ordered = totals
            .Where(x => states.ContainsKey(x.PlayerId))
            .OrderByDescending(x => x.XpGained)
            .ThenBy(x => states[x.PlayerId].Username, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.PlayerId)
            .ToArray();
        var skip = Math.Min((long)(page - 1) * pageSize, int.MaxValue);
        var entries = ordered.Skip((int)skip).Take(pageSize)
            .Select((x, index) =>
            {
                var state = states[x.PlayerId];
                return new LeaderboardEntry((int)skip + index + 1, x.PlayerId, state.Username,
                    x.XpGained, state.LastSampledAtUtc, state.NeedsBaseline ||
                    now - state.LastSampledAtUtc > staleAfter);
            })
            .ToArray();
        return new LeaderboardPage(period, start, end, page, pageSize, ordered.Length, now, entries);
    }

    public Task DeleteGainEventsBeforeAsync(DateTimeOffset cutoffUtc, CancellationToken ct) =>
        db.XpGainEvents.Where(x => x.ObservedAtUtc < cutoffUtc.ToUniversalTime()).ExecuteDeleteAsync(ct);
}
