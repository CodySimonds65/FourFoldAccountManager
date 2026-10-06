using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Core.LiveFeed;

// The evidence for trusting the feed with the XP tracker later: at each profile poll, the XP the website shows gained
// must equal the XP the feed's battle results reported over the same window. Counts only; it changes nothing. Not
// thread-safe: the feed calls it under its own lock.
public sealed class XpReconciler
{
    private readonly Dictionary<Guid, List<(DateTimeOffset At, string ClassName, long Exp)>> _results = [];
    private readonly Dictionary<Guid, (DateTimeOffset At, PlayerProgressSnapshot Snapshot)> _lastSamples = [];

    // Each account's last unmatched difference. The game saves XP and sends the result at about the same moment, so a
    // battle that ends just before a poll can be counted a window late: +x, then -x. That pair counts as two matches.
    private readonly Dictionary<Guid, long> _openMismatches = [];

    public int Matched { get; private set; }

    public int Mismatched { get; private set; }

    public int Skipped { get; private set; }

    public void RecordResult(Guid accountId, DateTimeOffset at, string className, long expGained)
    {
        if (!_results.TryGetValue(accountId, out var results))
        {
            _results[accountId] = results = [];
        }

        results.Add((at, className, expGained));
    }

    // feedActiveSince: when the feed last became active, or null when it isn't active now.
    public void RecordSample(
        Guid accountId, PlayerProgressSnapshot snapshot, DateTimeOffset sampledAt, DateTimeOffset? feedActiveSince)
    {
        var hadPrevious = _lastSamples.TryGetValue(accountId, out var previous);
        _lastSamples[accountId] = (sampledAt, snapshot);
        var results = _results.GetValueOrDefault(accountId) ?? [];
        var inWindow = hadPrevious
            ? results.Where(result => result.At > previous.At && result.At <= sampledAt).ToArray()
            : [];
        results.RemoveAll(result => result.At <= sampledAt);

        // Only a window the feed watched from start to end, on one class at one level, can be compared exactly.
        if (!hadPrevious || feedActiveSince is not { } since || since > previous.At ||
            ActiveClass(previous.Snapshot) is not { } before || ActiveClass(snapshot) is not { } after ||
            !string.Equals(before.Name, after.Name, StringComparison.OrdinalIgnoreCase) ||
            before.Class.Level != after.Class.Level)
        {
            Skipped++;
            _openMismatches.Remove(accountId);
            return;
        }

        var polled = after.Class.CurrentXp - before.Class.CurrentXp;
        var fed = inWindow
            .Where(result => string.Equals(result.ClassName, after.Name, StringComparison.OrdinalIgnoreCase))
            .Sum(result => result.Exp);
        var difference = fed - polled;
        if (difference == 0)
        {
            Matched++;
            _openMismatches.Remove(accountId);
            return;
        }

        if (_openMismatches.Remove(accountId, out var open) && open == -difference)
        {
            Mismatched--;
            Matched += 2;
            return;
        }

        Mismatched++;
        _openMismatches[accountId] = difference;
    }

    private static (string Name, ClassProfileSnapshot Class)? ActiveClass(PlayerProgressSnapshot snapshot) =>
        snapshot.ActiveClassName is { } name && snapshot.Classes.TryGetValue(name, out var found) ? (name, found) : null;
}
