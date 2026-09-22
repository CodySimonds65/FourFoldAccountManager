namespace FourFoldAccountManager.Core.Tracking;

public sealed class TrackedPlayerIdentity(string? username, int? configuredPlayerId)
{
    public string? Username { get; } = username;
    public int? ConfiguredPlayerId { get; } = configuredPlayerId;
    public int? ResolvedPlayerId { get; set; } = configuredPlayerId;

    public bool MatchesConfiguration(string? username, int? configuredPlayerId) =>
        string.Equals(Username, username, StringComparison.OrdinalIgnoreCase) &&
        ConfiguredPlayerId == configuredPlayerId;
}
