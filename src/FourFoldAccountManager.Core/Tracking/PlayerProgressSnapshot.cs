namespace FourFoldAccountManager.Core.Tracking;

public sealed record PlayerProgressSnapshot(
    string Username,
    string? ActiveClassName,
    IReadOnlyDictionary<string, ClassXpSnapshot> Classes,
    IReadOnlyList<string> InvalidClasses);
