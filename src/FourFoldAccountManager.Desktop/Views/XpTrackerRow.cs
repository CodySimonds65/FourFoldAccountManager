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
            state.Status,
            state.LastUpdated is { } updated
                ? $"Updated {updated.ToLocalTime():h:mm:ss tt}"
                : "Waiting for first snapshot");
    }
}
