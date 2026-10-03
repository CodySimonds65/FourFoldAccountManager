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
