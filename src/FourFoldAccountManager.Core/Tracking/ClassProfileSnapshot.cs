namespace FourFoldAccountManager.Core.Tracking;

public sealed record ClassProfileSnapshot(
    int Level,
    long CurrentXp,
    long NextLevelXp,
    string? SourceUpdated)
{
    public string? ClassName { get; init; }
    public long? Hp { get; init; }
    public long? Sp { get; init; }
    public long? Attack { get; init; }
    public long? Magic { get; init; }
    public long? Skill { get; init; }
    public long? Speed { get; init; }
    public long? Defense { get; init; }
    public long? Resistance { get; init; }
    public long? Luck { get; init; }
    public IReadOnlyDictionary<string, string> Equipment { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
