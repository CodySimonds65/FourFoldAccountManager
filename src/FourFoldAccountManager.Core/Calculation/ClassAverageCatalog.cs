namespace FourFoldAccountManager.Core.Calculation;

public sealed class ClassAverageCatalog
{
    private static readonly CharacterStat[] OrderedStats = Enum.GetValues<CharacterStat>();
    private readonly IReadOnlyDictionary<string, ClassAverageDefinition> _definitions;

    public ClassAverageCatalog() : this(BuildDefaultDefinitions()) { }

    public ClassAverageCatalog(IReadOnlyDictionary<string, ClassAverageDefinition> definitions)
    {
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
    }

    public bool TryGet(string className, out ClassAverageDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            definition = null!;
            return false;
        }

        return _definitions.TryGetValue(className.Trim(), out definition!);
    }

    private static IReadOnlyDictionary<string, ClassAverageDefinition> BuildDefaultDefinitions()
    {
        return new Dictionary<string, ClassAverageDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["Charmer"] = Define("Charmer", "Spring",
                [50, 85, 5, 5, 10, 7, 5, 3, 5],
                [49.5, 8, .45, .45, .55, .75, .50, .25, .40]),
            ["Evergreen Soldier"] = Define("Evergreen Soldier", "Spring",
                [75, 45, 7, 2, 8, 5, 5, 6, 2],
                [74.5, 4, .65, .15, .80, .40, .30, .45, .25]),
            ["Scout"] = Define("Scout", "Spring",
                [52, 85, 7, 2, 9, 12, 4, 4, 2],
                [51.5, 8, .50, .10, .65, .95, .30, .30, .25]),
            ["Shaman"] = Define("Shaman", "Spring",
                [60, 70, 2, 8, 7, 5, 6, 2, 8],
                [59.5, 6.5, .15, .55, .90, .30, .20, .20, .75]),
            ["Desert Bandit"] = Define("Desert Bandit", "Summer",
                [60, 70, 6, 1, 9, 8, 6, 5, 3],
                [59.5, 6.5, .65, .20, .90, .55, .35, .35, .30]),
            ["Hypnotist"] = Define("Hypnotist", "Summer",
                [55, 80, 2, 7, 10, 5, 5, 5, 5],
                [54.5, 7.5, .15, .55, .40, .30, .25, .60, .75]),
            ["Pathfinder"] = Define("Pathfinder", "Summer",
                [60, 70, 6, 2, 7, 10, 5, 5, 3],
                [59.5, 6.5, .55, .15, .75, .75, .35, .35, .15]),
            ["Sun Witch"] = Define("Sun Witch", "Summer",
                [52, 85, 1, 9, 9, 6, 5, 2, 8],
                [52, 8, .15, .75, .60, .40, .15, .30, .75]),
            ["Clown"] = Define("Clown", "Fall",
                [65, 60, 7, 6, 5, 6, 4, 7, 2],
                [64, 5.5, .65, .65, .55, .50, .25, .40, .40]),
            ["Harvest Soldier"] = Define("Harvest Soldier", "Fall",
                [67, 55, 8, 2, 8, 6, 3, 7, 3],
                [66, 5, .70, .25, .50, .45, .30, .50, .30]),
            ["Savage"] = Define("Savage", "Fall",
                [62, 65, 8, 2, 7, 9, 4, 5, 3],
                [61.5, 6, .60, .20, .65, .80, .25, .35, .25]),
            ["Thaumaturgist"] = Define("Thaumaturgist", "Fall",
                [62, 65, 2, 8, 6, 5, 7, 4, 6],
                [61.5, 6, .20, .85, .35, .20, .55, .40, .45]),
            ["Arctic Soldier"] = Define("Arctic Soldier", "Winter",
                [80, 40, 9, 2, 6, 2, 5, 8, 2],
                [79.5, 3.5, .80, .20, .60, .30, .30, .75, .10]),
            ["Clairvoyant"] = Define("Clairvoyant", "Winter",
                [50, 90, 1, 7, 9, 7, 4, 2, 10],
                [49.5, 8.5, .05, .70, .50, .35, .35, .15, .85]),
            ["Medicine Man"] = Define("Medicine Man", "Winter",
                [65, 60, 6, 7, 5, 6, 1, 5, 7],
                [65, 5.5, .60, .70, .45, .30, .05, .30, .60]),
            ["Snow Bandit"] = Define("Snow Bandit", "Winter",
                [57, 70, 6, 2, 9, 9, 4, 5, 4],
                [56.5, 6.5, .65, .15, .80, .60, .35, .30, .35])
        };
    }

    private static ClassAverageDefinition Define(
        string name,
        string season,
        double[] baseValues,
        double[] growthValues)
    {
        return new ClassAverageDefinition(name, season,
            OrderedStats.Zip(baseValues).ToDictionary(pair => pair.First, pair => pair.Second),
            OrderedStats.Zip(growthValues).ToDictionary(pair => pair.First, pair => pair.Second));
    }
}
