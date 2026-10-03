namespace FourFoldAccountManager.Core.Tracking;

public sealed record PlayerProgressSnapshot(
    string Username,
    string? ActiveClassName,
    IReadOnlyDictionary<string, ClassProfileSnapshot> Classes,
    IReadOnlyList<string> InvalidClasses);
