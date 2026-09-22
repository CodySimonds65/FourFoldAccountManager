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

                long gain;
                if (newClass.Level == oldClass.Level)
                {
                    gain = newClass.CurrentXp - oldClass.CurrentXp;
                    if (gain < 0)
                    {
                        invalid.Add(name);
                        continue;
                    }
                }
                else
                {
                    var crossed = SumCaps(oldClass.Level + 1L, newClass.Level - 1L);
                    var exact = new BigInteger(oldClass.NextLevelXp - oldClass.CurrentXp) + crossed + newClass.CurrentXp;
                    if (exact > long.MaxValue)
                    {
                        invalid.Add(name);
                        continue;
                    }

                    gain = (long)exact;
                }

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

    private static long Cap(int level) => checked(5L * level * (level + 1L));

    private static BigInteger SumCaps(long first, long last)
    {
        if (last < first)
        {
            return BigInteger.Zero;
        }

        static BigInteger Prefix(long level) =>
            5 * (BigInteger)level * (level + 1) * (level + 2) / 3;
        return Prefix(last) - Prefix(first - 1);
    }
}
