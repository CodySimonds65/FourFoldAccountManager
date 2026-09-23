namespace FourFoldAccountManager.Core.Tracking;

public sealed record PlayerProgressSnapshot
{
    public PlayerProgressSnapshot(
        string username,
        string? activeClassName,
        IReadOnlyDictionary<string, ClassProfileSnapshot> classes,
        IReadOnlyList<string> invalidClasses,
        IReadOnlyList<string>? invalidStatClasses = null)
    {
        Username = username;
        ActiveClassName = activeClassName;
        Classes = classes;
        InvalidClasses = invalidClasses;
        InvalidStatClasses = invalidStatClasses ?? Array.Empty<string>();
    }

    public string Username { get; init; }
    public string? ActiveClassName { get; init; }
    public IReadOnlyDictionary<string, ClassProfileSnapshot> Classes { get; init; }
    public IReadOnlyList<string> InvalidClasses { get; init; }
    public IReadOnlyList<string> InvalidStatClasses { get; init; }
}
