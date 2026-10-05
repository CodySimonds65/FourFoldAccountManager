namespace FourFoldAccountManager.Core.Tracking;

public sealed record PlayerProgressSnapshot(
    string Username,
    string? ActiveClassName,
    IReadOnlyDictionary<string, ClassProfileSnapshot> Classes,
    IReadOnlyList<string> InvalidClasses)
{
    // From the profile's game-stats strip. Null when the page doesn't give them; nothing that tracks XP needs them.
    public long? Silver { get; init; }
    public long? Gold { get; init; }
    public string? Location { get; init; }
}
