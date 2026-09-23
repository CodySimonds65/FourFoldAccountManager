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
    int EqualCount,
    double? MeanPercentageDifference)
{
    public static ClassComparisonDisplayState FromSnapshot(
        PlayerProgressSnapshot snapshot,
        ClassComparisonCalculator? calculator = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(snapshot.ActiveClassName))
        {
            return Unavailable("The active class is unavailable.");
        }

        var className = snapshot.ActiveClassName.Trim();
        var active = snapshot.Classes.FirstOrDefault(pair =>
            string.Equals(pair.Key, className, StringComparison.OrdinalIgnoreCase)).Value;
        if (active is null)
        {
            return Unavailable("The selected profile does not contain its active class.", className);
        }

        var comparison = (calculator ?? new ClassComparisonCalculator()).Compare(active);
        if (!comparison.IsAvailable)
        {
            return Unavailable(comparison.UnavailableReason ?? "Class comparison is unavailable.", className,
                active.Level, active.SourceUpdated);
        }

        return new ClassComparisonDisplayState(
            true,
            "Profile stats loaded.",
            className,
            active.Level,
            active.SourceUpdated,
            comparison.Rows,
            comparison.AboveCount,
            comparison.BelowCount,
            comparison.EqualCount,
            comparison.MeanPercentageDifference);
    }

    private static ClassComparisonDisplayState Unavailable(
        string status,
        string? className = null,
        int? level = null,
        string? sourceUpdated = null) =>
        new(false, status, className, level, sourceUpdated, Array.Empty<ClassStatComparison>(), 0, 0, 0, null);
}
