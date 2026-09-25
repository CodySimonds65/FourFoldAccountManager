using System.Globalization;
using FourFoldAccountManager.Core.Calculation;
using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Core.Overlay;

// Text for the full-screen XP calc card: XP and levels left to the saved target, or to the next level.
public sealed record XpCalcCardContent(
    bool IsAvailable,
    string TargetLine,
    string RemainingLine,
    string Status,
    long? TargetLevel)
{
    public const string WaitingStatus = "Waiting for profile…";

    public const string TargetReached = "Target reached";

    public static XpCalcCardContent FromSnapshot(PlayerProgressSnapshot? snapshot, long? savedTargetLevel)
    {
        if (snapshot is null)
        {
            return new XpCalcCardContent(false, string.Empty, string.Empty, WaitingStatus, null);
        }

        var state = ExperienceCalculatorState.FromSnapshot(snapshot);
        if (state.Profile is null)
        {
            return new XpCalcCardContent(false, string.Empty, string.Empty, state.Status, null);
        }

        var level = state.CurrentLevel;
        var classLine = string.Create(CultureInfo.InvariantCulture, $"{state.ClassName} {level}");
        if (savedTargetLevel is { } saved && saved > 0 && saved <= level)
        {
            return new XpCalcCardContent(true,
                string.Create(CultureInfo.InvariantCulture, $"{classLine} → {saved}"), TargetReached, string.Empty, saved);
        }

        var target = savedTargetLevel is { } wanted && wanted > level ? wanted : level + 1;
        var targetLine = string.Create(CultureInfo.InvariantCulture, $"{classLine} → {target}");
        var projected = state.WithTarget(target);
        if (!projected.IsValid)
        {
            return new XpCalcCardContent(false, targetLine, string.Empty, projected.Status, target);
        }

        var levels = projected.LevelsRemaining;
        var remaining = string.Create(CultureInfo.InvariantCulture,
            $"{projected.RemainingXpText} XP · {levels} {(levels == 1 ? "level" : "levels")} to go");
        return new XpCalcCardContent(true, targetLine, remaining, string.Empty, target);
    }
}
