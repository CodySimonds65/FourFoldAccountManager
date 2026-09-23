using System.Numerics;
using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Core.Calculation;

public static class ExperienceCurve
{
    public static BigInteger TotalXpAtLevel(long level)
    {
        if (level < 1) throw new ArgumentOutOfRangeException(nameof(level), "Level must be positive.");
        var value = new BigInteger(level);
        return 5 * (value * value * value - value) / 3;
    }

    public static BigInteger XpForNextLevel(long level)
    {
        if (level < 1) throw new ArgumentOutOfRangeException(nameof(level), "Level must be positive.");
        return 5 * (BigInteger)level * (level + 1);
    }

    public static BigInteger XpBetweenLevels(long currentLevel, long targetLevel)
    {
        if (currentLevel < 1) throw new ArgumentOutOfRangeException(nameof(currentLevel));
        if (targetLevel < currentLevel) throw new ArgumentOutOfRangeException(nameof(targetLevel));
        return TotalXpAtLevel(targetLevel) - TotalXpAtLevel(currentLevel);
    }

    public static ExperienceProjection Project(ClassProfileSnapshot profile, long targetLevel)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var currentLevel = profile.Level;
        if (currentLevel < 1 || targetLevel < 1)
            return ExperienceProjection.Invalid("Levels must be positive.", currentLevel, targetLevel);
        if (targetLevel < currentLevel)
            return ExperienceProjection.Invalid("Target level cannot be below the current level.", currentLevel, targetLevel);

        var currentStart = TotalXpAtLevel(currentLevel);
        var targetAbsolute = TotalXpAtLevel(targetLevel);
        var expectedCap = XpForNextLevel(currentLevel);
        var progressValid = profile.CurrentXp >= 0 &&
            expectedCap <= long.MaxValue &&
            profile.NextLevelXp == (long)expectedCap &&
            profile.CurrentXp < profile.NextLevelXp;
        var currentAbsolute = progressValid ? currentStart + profile.CurrentXp : currentStart;
        var remaining = BigInteger.Max(BigInteger.Zero, targetAbsolute - currentAbsolute);
        var transitions = new List<ExperienceTransition>();
        for (var level = (long)currentLevel; level < targetLevel; level++)
        {
            transitions.Add(new ExperienceTransition(level, level + 1, XpForNextLevel(level)));
        }

        return new ExperienceProjection(
            true,
            null,
            currentLevel,
            targetLevel,
            currentAbsolute,
            targetAbsolute,
            remaining,
            progressValid,
            progressValid ? "Current progress included" : "Current progress unavailable; using level start",
            transitions);
    }
}
