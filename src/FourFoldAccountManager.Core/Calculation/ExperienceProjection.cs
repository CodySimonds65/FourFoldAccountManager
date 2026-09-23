using System.Numerics;

namespace FourFoldAccountManager.Core.Calculation;

public sealed record ExperienceTransition(long FromLevel, long ToLevel, BigInteger XpRequired);

public sealed record ExperienceProjection(
    bool IsValid,
    string? Error,
    long CurrentLevel,
    long TargetLevel,
    BigInteger CurrentAbsoluteXp,
    BigInteger TargetAbsoluteXp,
    BigInteger RemainingXp,
    bool UsedCurrentProgress,
    string ProgressMessage,
    IReadOnlyList<ExperienceTransition> Transitions)
{
    public static ExperienceProjection Invalid(string error, long currentLevel, long targetLevel) =>
        new(false, error, currentLevel, targetLevel, BigInteger.Zero, BigInteger.Zero, BigInteger.Zero,
            false, error, Array.Empty<ExperienceTransition>());
}
