namespace FourFoldAccountManager.Core.Calculation;

public enum ComparisonDirection
{
    Above,
    Below,
    Equal
}

public sealed record ClassStatComparison(
    CharacterStat Stat,
    long ProfileValue,
    long Average,
    long Difference,
    double? PercentageDifference,
    ComparisonDirection Direction);

public sealed record ClassComparisonResult(
    bool IsAvailable,
    string? UnavailableReason,
    IReadOnlyList<ClassStatComparison> Rows,
    int AboveCount,
    int BelowCount,
    int EqualCount,
    double? MeanPercentageDifference)
{
    public static ClassComparisonResult Unavailable(string reason) =>
        new(false, reason, Array.Empty<ClassStatComparison>(), 0, 0, 0, null);
}
