using System.Globalization;
using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Core.Calculation;

public sealed record ExperienceCalculatorState(
    bool IsValid,
    string Status,
    string? ClassName,
    long CurrentLevel,
    long TargetLevel,
    string RemainingXpText,
    string CurrentAbsoluteXpText,
    string TargetAbsoluteXpText,
    long LevelsRemaining,
    bool UsedCurrentProgress,
    ExperienceProjection? Projection,
    ClassProfileSnapshot? Profile)
{
    public static ExperienceCalculatorState FromSnapshot(PlayerProgressSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(snapshot.ActiveClassName))
        {
            return Unavailable("The active class is unavailable.");
        }

        var activeName = snapshot.ActiveClassName.Trim();
        var profile = snapshot.Classes.FirstOrDefault(pair =>
            string.Equals(pair.Key, activeName, StringComparison.OrdinalIgnoreCase)).Value;
        return profile is null
            ? Unavailable("The selected profile does not contain its active class.", activeName)
            : FromProfile(profile);
    }

    public static ExperienceCalculatorState FromProfile(ClassProfileSnapshot profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Unselected(profile);
    }

    public ExperienceCalculatorState WithTarget(long targetLevel) => Profile switch
    {
        null => this with { TargetLevel = targetLevel },
        { } profile when targetLevel > XpCalculatorTargets.MaxTargetLevel => TooHigh(profile, targetLevel),
        { } profile => Create(profile, targetLevel)
    };

    public ExperienceCalculatorState WithoutTarget() =>
        Profile is null ? this : Unselected(Profile);

    private static ExperienceCalculatorState Unselected(ClassProfileSnapshot profile) =>
        new(false, "Enter a target level.", profile.ClassName, profile.Level, 0,
            "—", "—", "—", 0, false, null, profile);

    private static ExperienceCalculatorState Create(ClassProfileSnapshot profile, long targetLevel)
    {
        var projection = ExperienceCurve.Project(profile, targetLevel);
        var isValid = projection.IsValid;
        return new ExperienceCalculatorState(
            isValid,
            isValid ? projection.ProgressMessage : projection.Error ?? "XP calculation is unavailable.",
            profile.ClassName,
            profile.Level,
            targetLevel,
            isValid ? Format(projection.RemainingXp) : "—",
            isValid ? Format(projection.CurrentAbsoluteXp) : "—",
            isValid ? Format(projection.TargetAbsoluteXp) : "—",
            isValid ? targetLevel - profile.Level : 0,
            projection.UsedCurrentProgress,
            projection,
            profile);
    }

    // Above the cap, skip ExperienceCurve.Project entirely: it builds one transition per level.
    private static ExperienceCalculatorState TooHigh(ClassProfileSnapshot profile, long targetLevel) =>
        new(false, "Target level must be 9,999 or lower.", profile.ClassName, profile.Level, targetLevel,
            "—", "—", "—", 0, false, null, profile);

    private static ExperienceCalculatorState Unavailable(string status, string? className = null) =>
        new(false, status, className, 0, 0, "—", "—", "—", 0, false, null, null);

    private static string Format(System.Numerics.BigInteger value) =>
        value.ToString("N0", CultureInfo.InvariantCulture);
}
