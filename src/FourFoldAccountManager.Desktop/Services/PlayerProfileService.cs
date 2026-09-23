using System.IO;
using System.Net.Http;
using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Desktop.Services;

public sealed class PlayerProfileService
{
    private readonly IPlayerProfileTransport _transport;
    private readonly Dictionary<Guid, CachedProfile> _cache = [];
    private readonly object _cacheGate = new();

    public PlayerProfileService(IPlayerProfileTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public async Task<PlayerProfileReadResult> ReadAsync(
        Guid accountId,
        string? username,
        int? playerId,
        CancellationToken cancellationToken)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("An account ID is required.", nameof(accountId));
        }

        username = username?.Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            return Failure(PlayerProfileReadStatus.MissingUsername, "Add a ranking username to read the public profile.", accountId);
        }

        var cached = GetCached(accountId);
        var resolvedId = playerId is > 0 ? playerId : cached?.PlayerId;
        if (resolvedId is null)
        {
            IReadOnlyList<RankingEntry> ranking;
            try
            {
                ranking = await _transport.GetRankingAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return Failure(PlayerProfileReadStatus.RankingUnavailable,
                    "Ranking data is unavailable; link a profile or try again.", accountId);
            }

            var identity = RankingIdentityResolver.Resolve(username, ranking);
            if (identity.Status == IdentityResolutionStatus.Ambiguous)
            {
                return Failure(PlayerProfileReadStatus.Ambiguous,
                    "Multiple ranking matches found; link the exact player profile.", accountId);
            }

            if (identity.Status != IdentityResolutionStatus.Matched)
            {
                return Failure(PlayerProfileReadStatus.NotInTop200,
                    "Player is not in the top 200; link the player profile.", accountId);
            }

            resolvedId = identity.PlayerId;
        }

        PlayerProgressSnapshot snapshot;
        try
        {
            snapshot = await _transport.GetProfileAsync(resolvedId!.Value, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidDataException)
        {
            return Failure(PlayerProfileReadStatus.MalformedProfile,
                "The public profile is incomplete or malformed; try again later.", accountId, cached);
        }
        catch
        {
            return Failure(PlayerProfileReadStatus.ProfileUnavailable,
                "The public profile could not be refreshed; showing the last successful read.", accountId, cached);
        }

        if (!string.Equals(snapshot.Username.Trim(), username, StringComparison.OrdinalIgnoreCase))
        {
            Invalidate(accountId);
            return new PlayerProfileReadResult(
                PlayerProfileReadStatus.ProfileMismatch,
                null,
                null,
                "The public profile name does not match the ranking username.");
        }

        lock (_cacheGate)
        {
            _cache[accountId] = new CachedProfile(resolvedId.Value, snapshot);
        }

        return new PlayerProfileReadResult(PlayerProfileReadStatus.Success, resolvedId, snapshot, "Profile refreshed.");
    }

    public void Invalidate(Guid accountId)
    {
        lock (_cacheGate)
        {
            _cache.Remove(accountId);
        }
    }

    private CachedProfile? GetCached(Guid accountId)
    {
        lock (_cacheGate)
        {
            return _cache.TryGetValue(accountId, out var cached) ? cached : null;
        }
    }

    private PlayerProfileReadResult Failure(
        PlayerProfileReadStatus status,
        string message,
        Guid accountId,
        CachedProfile? cached = null)
    {
        cached ??= GetCached(accountId);
        return new PlayerProfileReadResult(status, cached?.PlayerId, cached?.Snapshot, message);
    }

    private sealed record CachedProfile(int PlayerId, PlayerProgressSnapshot Snapshot);
}
