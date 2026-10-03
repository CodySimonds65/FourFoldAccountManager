namespace FourFoldAccountManager.Core.Calculation;

public sealed record ClassAverageDefinition(
    IReadOnlyDictionary<CharacterStat, double> Base,
    IReadOnlyDictionary<CharacterStat, double> Growth)
{
    private static readonly CharacterStat[] OrderedStats = Enum.GetValues<CharacterStat>();

    // Grouped by season: Spring, Summer, Fall, Winter.
    private static readonly Dictionary<string, ClassAverageDefinition> Definitions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Charmer"] = Define(
                [50, 85, 5, 5, 10, 7, 5, 3, 5],
                [49.5, 8, .45, .45, .55, .75, .50, .25, .40]),
            ["Evergreen Soldier"] = Define(
                [75, 45, 7, 2, 8, 5, 5, 6, 2],
                [74.5, 4, .65, .15, .80, .40, .30, .45, .25]),
            ["Scout"] = Define(
                [52, 85, 7, 2, 9, 12, 4, 4, 2],
                [51.5, 8, .50, .10, .65, .95, .30, .30, .25]),
            ["Shaman"] = Define(
                [60, 70, 2, 8, 7, 5, 6, 2, 8],
                [59.5, 6.5, .15, .55, .90, .30, .20, .20, .75]),
            ["Desert Bandit"] = Define(
                [60, 70, 6, 1, 9, 8, 6, 5, 3],
                [59.5, 6.5, .65, .20, .90, .55, .35, .35, .30]),
            ["Hypnotist"] = Define(
                [55, 80, 2, 7, 10, 5, 5, 5, 5],
                [54.5, 7.5, .15, .55, .40, .30, .25, .60, .75]),
            ["Pathfinder"] = Define(
                [60, 70, 6, 2, 7, 10, 5, 5, 3],
                [59.5, 6.5, .55, .15, .75, .75, .35, .35, .15]),
            ["Sun Witch"] = Define(
                [52, 85, 1, 9, 9, 6, 5, 2, 8],
                [52, 8, .15, .75, .60, .40, .15, .30, .75]),
            ["Clown"] = Define(
                [65, 60, 7, 6, 5, 6, 4, 7, 2],
                [64, 5.5, .65, .65, .55, .50, .25, .40, .40]),
            ["Harvest Soldier"] = Define(
                [67, 55, 8, 2, 8, 6, 3, 7, 3],
                [66, 5, .70, .25, .50, .45, .30, .50, .30]),
            ["Savage"] = Define(
                [62, 65, 8, 2, 7, 9, 4, 5, 3],
                [61.5, 6, .60, .20, .65, .80, .25, .35, .25]),
            ["Thaumaturgist"] = Define(
                [62, 65, 2, 8, 6, 5, 7, 4, 6],
                [61.5, 6, .20, .85, .35, .20, .55, .40, .45]),
            ["Arctic Soldier"] = Define(
                [80, 40, 9, 2, 6, 2, 5, 8, 2],
                [79.5, 3.5, .80, .20, .60, .30, .30, .75, .10]),
            ["Clairvoyant"] = Define(
                [50, 90, 1, 7, 9, 7, 4, 2, 10],
                [49.5, 8.5, .05, .70, .50, .35, .35, .15, .85]),
            ["Medicine Man"] = Define(
                [65, 60, 6, 7, 5, 6, 1, 5, 7],
                [65, 5.5, .60, .70, .45, .30, .05, .30, .60]),
            ["Snow Bandit"] = Define(
                [57, 70, 6, 2, 9, 9, 4, 5, 4],
                [56.5, 6.5, .65, .15, .80, .60, .35, .30, .35])
        };

    // className must not be null; callers have already rejected blank names.
    public static bool TryGet(string className, out ClassAverageDefinition definition) =>
        Definitions.TryGetValue(className.Trim(), out definition!);

    public double Projected(CharacterStat stat, int level)
    {
        if (level < 1 || !Base.TryGetValue(stat, out var baseValue))
        {
            return double.NaN;
        }

        var growth = Growth.TryGetValue(stat, out var value) ? value : 0d;
        return baseValue + (level - 1d) * growth;
    }

    private static ClassAverageDefinition Define(double[] baseValues, double[] growthValues) =>
        new(OrderedStats.Zip(baseValues).ToDictionary(pair => pair.First, pair => pair.Second),
            OrderedStats.Zip(growthValues).ToDictionary(pair => pair.First, pair => pair.Second));
}
