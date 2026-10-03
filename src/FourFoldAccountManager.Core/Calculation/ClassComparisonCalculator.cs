using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Core.Calculation;

public static class ClassComparisonCalculator
{
    private static readonly CharacterStat[] OrderedStats = Enum.GetValues<CharacterStat>();

    public static ClassComparisonDisplayState Compare(string className, ClassProfileSnapshot profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ClassComparisonDisplayState Unavailable(string status) =>
            ClassComparisonDisplayState.Unavailable(status, className, profile.Level, profile.SourceUpdated);

        if (string.IsNullOrWhiteSpace(profile.ClassName))
            return Unavailable("The active class is unavailable.");
        if (profile.Level < 1)
            return Unavailable("The profile level is invalid.");
        if (!ClassAverageDefinition.TryGet(profile.ClassName, out var definition))
            return Unavailable($"No class average is available for {profile.ClassName}.");

        var rows = new List<ClassStatComparison>(OrderedStats.Length);
        foreach (var stat in OrderedStats)
        {
            if (!TryGetProfileValue(profile, stat, out var profileValue))
                return Unavailable($"The profile is missing {stat}.");

            var projected = definition.Projected(stat, profile.Level);
            if (!double.IsFinite(projected) || projected == 0 || projected > long.MaxValue || projected < long.MinValue)
                return Unavailable($"The class average for {stat} is invalid.");

            var average = checked((long)Math.Round(projected, MidpointRounding.AwayFromZero));
            var difference = checked(profileValue - average);
            var direction = difference > 0 ? ComparisonDirection.Above :
                difference < 0 ? ComparisonDirection.Below : ComparisonDirection.Equal;
            var percentage = (double)difference / average * 100d;
            rows.Add(new ClassStatComparison(stat, profileValue, average, difference, percentage, direction));
        }

        return new ClassComparisonDisplayState(
            true,
            "Profile stats loaded.",
            className,
            profile.Level,
            profile.SourceUpdated,
            rows,
            rows.Count(row => row.Direction == ComparisonDirection.Above),
            rows.Count(row => row.Direction == ComparisonDirection.Below),
            rows.Average(row => row.PercentageDifference ?? 0d));
    }

    private static bool TryGetProfileValue(ClassProfileSnapshot profile, CharacterStat stat, out long value)
    {
        var candidate = stat switch
        {
            CharacterStat.Hp => profile.Hp,
            CharacterStat.Sp => profile.Sp,
            CharacterStat.Attack => profile.Attack,
            CharacterStat.Magic => profile.Magic,
            CharacterStat.Skill => profile.Skill,
            CharacterStat.Speed => profile.Speed,
            CharacterStat.Luck => profile.Luck,
            CharacterStat.Defense => profile.Defense,
            CharacterStat.Resistance => profile.Resistance,
            _ => null
        };
        value = candidate.GetValueOrDefault();
        return candidate.HasValue;
    }
}
