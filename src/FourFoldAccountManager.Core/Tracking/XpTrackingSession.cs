using FourFoldAccountManager.Core.LiveFeed;

namespace FourFoldAccountManager.Core.Tracking;

public sealed class XpTrackingSession
{
    // A live rate needs this much watched time first, so the first fight can't read as millions an hour.
    public static readonly TimeSpan LiveRateMinimum = TimeSpan.FromSeconds(60);

    private readonly XpRateWindow _window = new();
    private bool _hasFailedPoll;
    private PlayerProgressSnapshot? _baselineSnapshot;
    private DateTimeOffset? _baselineSampledAt;

    // While the live game feed watches the account: the end of the last stretch counted, the watch start or the last
    // fight. Null when it isn't watching.
    private DateTimeOffset? _liveMark;

    // Set when a live stretch ends. The next poll only moves the baseline, because the fights counted that XP.
    private bool _rebase;

    // The active class's progress as the last fight left it. Laid over the last poll while it is the newer truth.
    private LiveProgress? _liveProgress;

    public PlayerProgressSnapshot? LastSnapshot { get; private set; }
    public DateTimeOffset? LastSuccessfulAt { get; private set; }
    public double? RatePerHour { get; private set; }
    public long SessionGain => _window.SessionGain;
    public IReadOnlyList<XpGainInterval> Intervals => _window.Intervals;
    // The last poll with the live progress laid over it, while that progress is the newer truth (see
    // LiveProgressApplies). Persistence, ProfileSampled and the reconciler use LastSnapshot, the raw poll.
    public PlayerProgressSnapshot? DisplaySnapshot =>
        LastSnapshot is { } polled && _liveProgress is { } live && LiveProgressApplies(polled, live)
            ? WithLiveProgress(polled, live)
            : LastSnapshot;
    public string? ActiveClassName => DisplaySnapshot?.ActiveClassName;
    public long? XpUntilNextLevel =>
        ActiveClassName is { } name && DisplaySnapshot?.Classes.TryGetValue(name, out var progress) == true
            ? progress.NextLevelXp - progress.CurrentXp
            : null;
    public double? HoursUntilNextLevel =>
        XpUntilNextLevel is { } remaining && RatePerHour is { } rate && double.IsFinite(rate) && rate > 0
            ? remaining / rate
            : null;
    public bool IsStale { get; private set; }
    public bool IsStopped { get; private set; }
    public bool IsLive => _liveMark is not null;
    public bool MissedPreviousSample { get; private set; }
    public bool ActiveClassUnavailable { get; private set; }
    public IReadOnlyList<string> InvalidClassNames { get; private set; } = [];

    public void ApplySnapshot(PlayerProgressSnapshot snapshot, DateTimeOffset sampledAt)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (IsStopped) throw new InvalidOperationException("The tracking session has stopped.");
        if (LastSuccessfulAt is { } previousTime && sampledAt <= previousTime)
            throw new ArgumentOutOfRangeException(nameof(sampledAt));

        // A poll can lag a fight by seconds, but never by two polls: from the second poll since it, the poll is the truth.
        if (_liveProgress is { } progress && LastSuccessfulAt is { } previousPoll && progress.At <= previousPoll)
        {
            _liveProgress = null;
        }

        MissedPreviousSample = _hasFailedPoll;
        ActiveClassUnavailable = string.IsNullOrWhiteSpace(snapshot.ActiveClassName);
        InvalidClassNames = [];
        if (IsLive || _rebase)
        {
            // The fights counted this XP: while live a poll adds no interval, and the first poll after a live stretch
            // is a baseline.
            _hasFailedPoll = false;
            if (!IsLive)
            {
                _rebase = false;
            }
        }
        else if (_hasFailedPoll || ActiveClassUnavailable)
        {
            // No trustworthy interval to measure after a missed poll or without an active class.
            _hasFailedPoll = false;
        }
        else if (_baselineSnapshot is { } baseline && _baselineSampledAt is { } from)
        {
            var activeClassName = snapshot.ActiveClassName!;
            var gain = XpProgressCalculator.Calculate(
                ForClass(baseline, activeClassName), ForClass(snapshot, activeClassName));
            InvalidClassNames = gain.InvalidClasses;
            if (gain.ValidClassCount > 0)
            {
                _window.Add(from, sampledAt, gain.ValidGain);
            }
        }
        else
        {
            var activeClassName = snapshot.ActiveClassName!;
            InvalidClassNames = snapshot.InvalidClasses.Contains(activeClassName,
                    StringComparer.OrdinalIgnoreCase) || !snapshot.Classes.ContainsKey(activeClassName)
                ? [activeClassName]
                : [];
        }

        RatePerHour = RateAt(sampledAt);
        LastSnapshot = snapshot;
        LastSuccessfulAt = sampledAt;
        _baselineSnapshot = snapshot;
        _baselineSampledAt = sampledAt;
        IsStale = false;
    }

    public void MarkFetchFailed()
    {
        if (!IsStopped)
        {
            IsStale = true;
            _hasFailedPoll = true;
        }
    }

    public void Stop() => IsStopped = true;

    // The live game feed watches the account's game socket from `at`. A stretch never starts before the last poll,
    // whose interval already counted the time up to it.
    public void BeginLive(DateTimeOffset at)
    {
        if (IsStopped || IsLive)
        {
            return;
        }

        _liveMark = LastSuccessfulAt is { } polled && polled > at ? polled : at;
    }

    public void EndLive()
    {
        if (!IsLive)
        {
            return;
        }

        _liveMark = null;
        _rebase = true;
    }

    // One fight's reward from the live game feed. It counts from the mark to this fight, so the time between fights is
    // in the rate too. A late or repeated result, or one already inside a poll's interval, adds nothing.
    public void ApplyLiveResult(
        DateTimeOffset at, string? className, long expGained, int reachedLevel, long expNeededToNextLevel)
    {
        if (IsStopped || _liveMark is not { } mark || at <= mark)
        {
            return;
        }

        _window.Add(mark, at, Math.Max(0, expGained));
        _liveMark = at;
        RatePerHour = RateAt(at);
        // The result carries the whole state: the level reached and the XP still needed. The game levels up at most once
        // per fight, so the level's total follows from the level.
        if (!string.IsNullOrWhiteSpace(className))
        {
            var level = Math.Max(1, reachedLevel);
            var total = LevelTotal(level);
            _liveProgress = new LiveProgress(
                className.Trim(), level, Math.Max(0, total - Math.Max(0, expNeededToNextLevel)), total, at);
        }
    }

    // At a poll or a fight. While live, the time since the last fight counts as gaining nothing, so the rate falls while
    // the account idles.
    private double? RateAt(DateTimeOffset at) =>
        _liveMark is { } mark ? _window.GetRate(at, mark, LiveRateMinimum) : _window.GetRate(at);

    private static PlayerProgressSnapshot ForClass(PlayerProgressSnapshot snapshot, string name)
    {
        var classes = new Dictionary<string, ClassProfileSnapshot>(StringComparer.OrdinalIgnoreCase);
        if (snapshot.Classes.TryGetValue(name, out var progress)) classes.Add(name, progress);
        return snapshot with
        {
            Classes = classes,
            InvalidClasses = snapshot.InvalidClasses.Where(value =>
                string.Equals(value, name, StringComparison.OrdinalIgnoreCase)).ToArray()
        };
    }

    public void ResetRate()
    {
        _window.ClearIntervals();
        _baselineSnapshot = null;
        _baselineSampledAt = null;
        _hasFailedPoll = false;
        RatePerHour = null;
        MissedPreviousSample = false;
        ActiveClassUnavailable = false;
        InvalidClassNames = [];
    }

    public void ResetAll()
    {
        ResetRate();
        _window.ResetSession();
    }

    // The game's ExpNeededForNextLevel: 5·L·(L+1), capped at int.MaxValue.
    private static long LevelTotal(int level)
    {
        long capped = Math.Min(level, 1_000_000);
        return Math.Min(5 * capped * (capped + 1), int.MaxValue);
    }

    // Newer than the last poll, or the poll's own active class and ahead of it: the poll that was saved just before
    // the fight landed.
    private bool LiveProgressApplies(PlayerProgressSnapshot polled, LiveProgress live)
    {
        if (LastSuccessfulAt is not { } polledAt || live.At > polledAt)
        {
            return true;
        }

        return polled.ActiveClassName is { } active &&
               XpReconciler.NormalizeClass(active) == XpReconciler.NormalizeClass(live.ClassName) &&
               polled.Classes.TryGetValue(active, out var current) &&
               (live.Level > current.Level || (live.Level == current.Level && live.CurrentXp > current.CurrentXp));
    }

    // The poll with the live class active and its level and XP from the fight. Its stats and equipment stay the
    // poll's. The class keeps the website's spelling when the poll lists it.
    private static PlayerProgressSnapshot WithLiveProgress(PlayerProgressSnapshot polled, LiveProgress live)
    {
        var key = polled.Classes.Keys.FirstOrDefault(name =>
            XpReconciler.NormalizeClass(name) == XpReconciler.NormalizeClass(live.ClassName)) ?? live.ClassName;
        var classes = new Dictionary<string, ClassProfileSnapshot>(polled.Classes, StringComparer.OrdinalIgnoreCase);
        classes[key] = classes.TryGetValue(key, out var existing)
            ? existing with { Level = live.Level, CurrentXp = live.CurrentXp, NextLevelXp = live.NextLevelXp }
            : new ClassProfileSnapshot(live.Level, live.CurrentXp, live.NextLevelXp, null);
        return polled with { ActiveClassName = key, Classes = classes };
    }

    private sealed record LiveProgress(string ClassName, int Level, long CurrentXp, long NextLevelXp, DateTimeOffset At);
}
