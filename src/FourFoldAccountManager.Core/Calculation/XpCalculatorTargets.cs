using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Core.Calculation;

// Per-account XP calculator target levels, typed in the sidebar and read by the XP calc overlay card.
public static class XpCalculatorTargets
{
    // ExperienceCurve.Project builds one transition per level, so an unbounded target can exhaust memory.
    public const long MaxTargetLevel = 9_999;

    public static long? Get(PanelSettings settings, Guid accountId)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.XpCalculatorTargetLevels.TryGetValue(accountId, out var level) ? level : null;
    }

    public static PanelSettings WithTarget(PanelSettings settings, Guid accountId, long? targetLevel)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("An account ID is required.", nameof(accountId));
        }

        if (targetLevel is <= 0 or > MaxTargetLevel)
        {
            throw new ArgumentOutOfRangeException(nameof(targetLevel), targetLevel,
                $"Target levels must be between 1 and {MaxTargetLevel}.");
        }

        var targets = new Dictionary<Guid, long>(settings.XpCalculatorTargetLevels);
        if (targetLevel is { } level)
        {
            targets[accountId] = level;
        }
        else
        {
            targets.Remove(accountId);
        }

        return settings with { XpCalculatorTargetLevels = targets };
    }

    public static IReadOnlyDictionary<Guid, long> Normalize(IReadOnlyDictionary<Guid, long>? targets) =>
        (targets ?? new Dictionary<Guid, long>())
            .Where(entry => entry.Key != Guid.Empty && entry.Value > 0 && entry.Value <= MaxTargetLevel)
            .ToDictionary(entry => entry.Key, entry => entry.Value);

    public static IReadOnlyDictionary<Guid, long> RemoveAccount(IReadOnlyDictionary<Guid, long> targets, Guid accountId)
    {
        ArgumentNullException.ThrowIfNull(targets);
        return targets.Where(entry => entry.Key != accountId).ToDictionary(entry => entry.Key, entry => entry.Value);
    }
}
