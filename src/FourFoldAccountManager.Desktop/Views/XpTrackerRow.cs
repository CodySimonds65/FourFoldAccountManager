using System.Globalization;
using FourFoldAccountManager.Desktop.Services;

namespace FourFoldAccountManager.Desktop.Views;

public sealed record XpTrackerRow(
    Guid AccountId,
    int SlotNumber,
    string DisplayName,
    string XpPerHourText,
    string SessionGainText,
    string ActiveClassText,
    string XpUntilNextLevelText,
    string TimeUntilNextLevelText,
    string StatusText,
    string LastUpdatedText)
{
    public static XpTrackerRow FromState(int slotNumber, string label, XpTrackerState state)
    {
        var culture = CultureInfo.CurrentCulture;
        return new XpTrackerRow(
            state.AccountId,
            slotNumber,
            state.Username ?? label,
            state.RatePerHour is { } rate ? $"{rate.ToString("N0", culture)} XP/hr" : "— XP/hr",
            $"+{state.SessionGain.ToString("N0", culture)} XP this session",
            state.ActiveClass ?? "Active class unknown",
            state.XpUntilNextLevel is { } remaining
                ? $"{remaining.ToString("N0", culture)} XP to next level"
                : "— XP to next level",
            FormatTimeUntilNextLevel(state.HoursUntilNextLevel),
            state.Status,
            state.LastUpdated is { } updated
                ? $"Updated {updated.ToLocalTime():h:mm:ss tt}"
                : "Waiting for first snapshot");
    }

    private static string FormatTimeUntilNextLevel(double? hoursUntilNextLevel)
    {
        if (hoursUntilNextLevel is not { } hours) return "— time to next level";

        var totalMinutes = Math.Ceiling(hours * 60);
        if (hours * 60 < 1) return "<1m time to next level";
        if (totalMinutes < 60) return $"{totalMinutes:0}m time to next level";

        var totalHours = (long)(totalMinutes / 60);
        if (totalHours < 24) return $"{totalHours}h {totalMinutes % 60:0}m time to next level";

        return $"{totalHours / 24}d {totalHours % 24}h time to next level";
    }
}
