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

    public PlayerProgressSnapshot? LastSnapshot { get; private set; }
    public DateTimeOffset? LastSuccessfulAt { get; private set; }
    public double? RatePerHour { get; private set; }
    public long SessionGain => _window.SessionGain;
    public IReadOnlyList<XpGainInterval> Intervals => _window.Intervals;
    public string? ActiveClassName => LastSnapshot?.ActiveClassName;
    public long? XpUntilNextLevel =>
        ActiveClassName is { } name && LastSnapshot?.Classes.TryGetValue(name, out var progress) == true
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
}
