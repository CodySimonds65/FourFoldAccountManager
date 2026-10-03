namespace FourFoldAccountManager.Core.Tracking;

public sealed class XpTrackingSession
{
    private readonly XpRateWindow _window = new();
    private bool _hasFailedPoll;
    private PlayerProgressSnapshot? _baselineSnapshot;
    private DateTimeOffset? _baselineSampledAt;

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
        if (_hasFailedPoll || ActiveClassUnavailable)
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

        RatePerHour = _window.GetRate(sampledAt);
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
