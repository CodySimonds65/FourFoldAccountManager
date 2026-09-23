using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Leaderboard.Service.Collection;

public interface ILeaderboardPublicProfileSource
{
    Task<PlayerProgressSnapshot> FetchAsync(int playerId, CancellationToken ct);
}
