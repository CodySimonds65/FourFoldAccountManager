using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Core.Calculation;

public sealed class ClassComparisonCalculator
{
    private static readonly CharacterStat[] OrderedStats = Enum.GetValues<CharacterStat>();
    private readonly ClassAverageCatalog _catalog;

    public ClassComparisonCalculator(ClassAverageCatalog? catalog = null) => _catalog = catalog ?? new ClassAverageCatalog();

    public ClassComparisonResult Compare(ClassProfileSnapshot profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(profile.ClassName))
            return ClassComparisonResult.Unavailable("The active class is unavailable.");
        if (profile.Level < 1)
            return ClassComparisonResult.Unavailable("The profile level is invalid.");
        if (!_catalog.TryGet(profile.ClassName, out var definition))
            return ClassComparisonResult.Unavailable($"No class average is available for {profile.ClassName}.");

        var rows = new List<ClassStatComparison>(OrderedStats.Length);
        foreach (var stat in OrderedStats)
        {
            if (!TryGetProfileValue(profile, stat, out var profileValue))
                return ClassComparisonResult.Unavailable($"The profile is missing {stat}.");

            var projected = definition.Projected(stat, profile.Level);
            if (!double.IsFinite(projected) || projected == 0 || projected > long.MaxValue || projected < long.MinValue)
                return ClassComparisonResult.Unavailable($"The class average for {stat} is invalid.");

            var average = checked((long)Math.Round(projected, MidpointRounding.AwayFromZero));
            var difference = checked(profileValue - average);
            var direction = difference > 0 ? ComparisonDirection.Above :
                difference < 0 ? ComparisonDirection.Below : ComparisonDirection.Equal;
            var percentage = (double)difference / average * 100d;
            rows.Add(new ClassStatComparison(stat, profileValue, average, difference, percentage, direction));
        }

        return new ClassComparisonResult(
            true,
            null,
            rows,
            rows.Count(row => row.Direction == ComparisonDirection.Above),
            rows.Count(row => row.Direction == ComparisonDirection.Below),
            rows.Count(row => row.Direction == ComparisonDirection.Equal),
            rows.Count == 0 ? null : rows.Average(row => row.PercentageDifference ?? 0d));
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
