using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Desktop.Services;

public interface IPlayerProfileTransport
{
    Task<IReadOnlyList<RankingEntry>> GetRankingAsync(CancellationToken cancellationToken);

    Task<PlayerProgressSnapshot> GetProfileAsync(int playerId, CancellationToken cancellationToken);
}
