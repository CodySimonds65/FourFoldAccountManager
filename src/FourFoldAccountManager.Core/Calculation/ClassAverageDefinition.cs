namespace FourFoldAccountManager.Core.Calculation;

public sealed record ClassAverageDefinition(
    string Name,
    string Season,
    IReadOnlyDictionary<CharacterStat, double> Base,
    IReadOnlyDictionary<CharacterStat, double> Growth)
{
    public double Projected(CharacterStat stat, int level)
    {
        if (level < 1 || !Base.TryGetValue(stat, out var baseValue))
        {
            return double.NaN;
        }

        var growth = Growth.TryGetValue(stat, out var value) ? value : 0d;
        return baseValue + (level - 1d) * growth;
    }
}
