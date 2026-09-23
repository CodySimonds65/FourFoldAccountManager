using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Desktop.Services;

public enum PlayerProfileReadStatus
{
    Success,
    MissingUsername,
    RankingUnavailable,
    Ambiguous,
    NotInTop200,
    ProfileUnavailable,
    ProfileMismatch,
    MalformedProfile
}

public sealed record PlayerProfileReadResult(
    PlayerProfileReadStatus Status,
    int? PlayerId,
    PlayerProgressSnapshot? Snapshot,
    string Message)
{
    public bool IsSuccess => Status == PlayerProfileReadStatus.Success;
}
