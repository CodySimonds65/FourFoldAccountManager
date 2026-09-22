using System.Numerics;

namespace FourFoldAccountManager.Core.Tracking;

public sealed record XpGainResult(long ValidGain, int ValidClassCount, IReadOnlyList<string> InvalidClasses);

public static class XpProgressCalculator
{
    public static XpGainResult Calculate(PlayerProgressSnapshot before, PlayerProgressSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var invalid = new HashSet<string>(before.InvalidClasses, StringComparer.OrdinalIgnoreCase);
        invalid.UnionWith(after.InvalidClasses);
        long total = 0;
        var validClassCount = 0;

        foreach (var (name, oldClass) in before.Classes)
        {
            if (!after.Classes.TryGetValue(name, out var newClass))
            {
                invalid.Add(name);
                continue;
            }

            try
            {
                if (oldClass.Level < 1 || newClass.Level < 1 ||
                    oldClass.NextLevelXp != Cap(oldClass.Level) ||
                    newClass.NextLevelXp != Cap(newClass.Level) ||
                    oldClass.CurrentXp < 0 || newClass.CurrentXp < 0 ||
                    oldClass.CurrentXp >= oldClass.NextLevelXp ||
                    newClass.CurrentXp >= newClass.NextLevelXp ||
                    newClass.Level < oldClass.Level)
                {
                    invalid.Add(name);
                    continue;
                }

                var exact = AbsoluteProgress(newClass) - AbsoluteProgress(oldClass);
                if (exact < 0 || exact > long.MaxValue)
                {
                    invalid.Add(name);
                    continue;
                }

                var gain = (long)exact;

                total = checked(total + gain);
                validClassCount++;
            }
            catch (OverflowException)
            {
                invalid.Add(name);
            }
        }

        foreach (var name in after.Classes.Keys)
        {
            if (!before.Classes.ContainsKey(name))
            {
                invalid.Add(name);
            }
        }

        return new XpGainResult(total, validClassCount,
            invalid.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static BigInteger Cap(int level) => 5 * (BigInteger)level * (level + 1L);

    private static BigInteger AbsoluteProgress(ClassXpSnapshot value) =>
        Prefix(value.Level - 1L) + value.CurrentXp;

    private static BigInteger Prefix(long level) => level <= 0
        ? BigInteger.Zero
        : 5 * (BigInteger)level * (level + 1) * (level + 2) / 3;
}
