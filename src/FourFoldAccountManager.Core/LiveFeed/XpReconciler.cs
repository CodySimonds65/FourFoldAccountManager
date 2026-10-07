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

    // Each account's compared windows in a row where the poll gained XP and the feed reported none. A game update that
    // changes only the battle result leaves the feed active with no results; one such window can be the timing pair.
    private readonly Dictionary<Guid, int> _missedResults = [];

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

    // watchedSince: when the feed started watching this account's current game socket, or null when it isn't watching.
    // True when battle results have stopped arriving for this account: the feed should stop being trusted.
    public bool RecordSample(
        Guid accountId, PlayerProgressSnapshot snapshot, DateTimeOffset sampledAt, DateTimeOffset? watchedSince)
    {
        var hadPrevious = _lastSamples.TryGetValue(accountId, out var previous);
        _lastSamples[accountId] = (sampledAt, snapshot);
        var results = _results.GetValueOrDefault(accountId) ?? [];
        var inWindow = hadPrevious
            ? results.Where(result => result.At > previous.At && result.At <= sampledAt).ToArray()
            : [];
        results.RemoveAll(result => result.At <= sampledAt);

        // Only a window the feed watched from start to end, on one class at one level, can be compared exactly.
        if (!hadPrevious || watchedSince is not { } since || since > previous.At ||
            ActiveClass(previous.Snapshot) is not { } before || ActiveClass(snapshot) is not { } after ||
            !string.Equals(NormalizeClass(before.Name), NormalizeClass(after.Name), StringComparison.Ordinal) ||
            before.Class.Level != after.Class.Level)
        {
            Skipped++;
            // A skipped window breaks the run too: a stale miss from before a level-up, a reconnect or the feed
            // switching back on must not pair with a later one.
            _openMismatches.Remove(accountId);
            _missedResults.Remove(accountId);
            return false;
        }

        var polled = after.Class.CurrentXp - before.Class.CurrentXp;
        var fed = inWindow
            .Where(result => string.Equals(
                NormalizeClass(result.ClassName), NormalizeClass(after.Name), StringComparison.Ordinal))
            .Sum(result => result.Exp);
        if (polled == 0 && fed == 0)
        {
            // No battle and no XP change proves nothing about the feed, so it isn't evidence either way.
            Skipped++;
            // A skipped window breaks the run too: a stale miss from before a level-up, a reconnect or the feed
            // switching back on must not pair with a later one.
            _openMismatches.Remove(accountId);
            _missedResults.Remove(accountId);
            return false;
        }

        // A silent window that opens a late pair is followed by one where its result arrives, and one that closes an
        // early pair doesn't count, so timing pairs can't make two in a row. A real break can: -b never cancels -a.
        var difference = fed - polled;
        var closesPair = _openMismatches.TryGetValue(accountId, out var open) && open == -difference;
        var stopped = false;
        // Silent means no battle result arrived at all, whatever its class: a class the game spells differently from
        // the website must not read as the feed missing every fight.
        if (polled > 0 && inWindow.Length == 0 && !closesPair)
        {
            var missed = _missedResults.GetValueOrDefault(accountId) + 1;
            stopped = missed >= 2;
            if (stopped)
            {
                _missedResults.Remove(accountId);
            }
            else
            {
                _missedResults[accountId] = missed;
            }
        }
        else
        {
            _missedResults.Remove(accountId);
        }

        if (difference == 0)
        {
            Matched++;
            _openMismatches.Remove(accountId);
            return stopped;
        }

        _openMismatches.Remove(accountId);
        if (closesPair)
        {
            Mismatched--;
            Matched += 2;
            return stopped;
        }

        Mismatched++;
        _openMismatches[accountId] = difference;
        return stopped;
    }

    // Matches the game's SkillCatalog.Normalize, so "Dark_Knight" and "dark knight" are the same class.
    internal static string NormalizeClass(string? name) => (name ?? string.Empty).Trim().Replace('_', ' ').ToLowerInvariant();

    private static (string Name, ClassProfileSnapshot Class)? ActiveClass(PlayerProgressSnapshot snapshot) =>
        snapshot.ActiveClassName is { } name && snapshot.Classes.TryGetValue(name, out var found) ? (name, found) : null;
}
