using System.Globalization;
using FourFoldAccountManager.Core.Calculation;
using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Core.Overlay;

public sealed record StatsCardRow(
    string StatLabel,
    string ProfileText,
    string AverageText,
    string DeltaText,
    ComparisonDirection Direction);

// Text for the full-screen Stats card: the active class's comparison against its class average.
public sealed record StatsCardContent(
    bool IsAvailable,
    string Header,
    string CountsText,
    string SummaryText,
    string Status,
    IReadOnlyList<StatsCardRow> Rows)
{
    public const string WaitingStatus = "Waiting for profile…";

    public static StatsCardContent FromSnapshot(PlayerProgressSnapshot? snapshot) =>
        snapshot is null
            ? new StatsCardContent(false, string.Empty, string.Empty, string.Empty, WaitingStatus, [])
            : FromState(ClassComparisonDisplayState.FromSnapshot(snapshot));

    public static StatsCardContent FromState(ClassComparisonDisplayState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var header = state.ActiveClassName is null
            ? string.Empty
            : state.Level is { } level
                ? string.Create(CultureInfo.InvariantCulture, $"{state.ActiveClassName} {level}")
                : state.ActiveClassName;
        if (!state.IsAvailable)
        {
            return new StatsCardContent(false, header, string.Empty, string.Empty, state.Status, []);
        }

        var counts = string.Create(CultureInfo.InvariantCulture, $"▲{state.AboveCount} ▼{state.BelowCount}");
        var summary = state.MeanPercentageDifference is { } mean ? $"{counts} · mean {Percent(mean)}" : counts;
        var rows = state.Rows.Select(row => new StatsCardRow(
                CharacterStatLabels.Short(row.Stat),
                row.ProfileValue.ToString("N0", CultureInfo.InvariantCulture),
                row.Average.ToString("N0", CultureInfo.InvariantCulture),
                row.PercentageDifference is { } percentage ? Percent(percentage) : "—",
                row.Direction))
            .ToArray();
        return new StatsCardContent(true, header, counts, summary, string.Empty, rows);
    }

    private static string Percent(double value) =>
        value.ToString("+#,##0.0;-#,##0.0;0.0", CultureInfo.InvariantCulture) + "%";
}
