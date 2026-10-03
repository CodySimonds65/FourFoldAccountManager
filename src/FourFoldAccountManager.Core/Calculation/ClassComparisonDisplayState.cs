using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Core.Calculation;

public sealed record ClassComparisonDisplayState(
    bool IsAvailable,
    string Status,
    string? ActiveClassName,
    int? Level,
    string? SourceUpdated,
    IReadOnlyList<ClassStatComparison> Rows,
    int AboveCount,
    int BelowCount,
    double? MeanPercentageDifference)
{
    public static ClassComparisonDisplayState FromSnapshot(PlayerProgressSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(snapshot.ActiveClassName))
        {
            return Unavailable("The active class is unavailable.");
        }

        var className = snapshot.ActiveClassName.Trim();
        var active = snapshot.Classes.FirstOrDefault(pair =>
            string.Equals(pair.Key, className, StringComparison.OrdinalIgnoreCase)).Value;
        return active is null
            ? Unavailable("The selected profile does not contain its active class.", className)
            : ClassComparisonCalculator.Compare(className, active);
    }

    internal static ClassComparisonDisplayState Unavailable(
        string status,
        string? className = null,
        int? level = null,
        string? sourceUpdated = null) =>
        new(false, status, className, level, sourceUpdated, Array.Empty<ClassStatComparison>(), 0, 0, null);
}
