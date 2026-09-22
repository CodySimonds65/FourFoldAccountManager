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
    public bool HasUncertainInterval { get; private set; }

    public void ApplySnapshot(PlayerProgressSnapshot snapshot, DateTimeOffset sampledAt)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (IsStopped) throw new InvalidOperationException("The tracking session has stopped.");
        if (LastSuccessfulAt is { } previousTime && sampledAt <= previousTime)
            throw new ArgumentOutOfRangeException(nameof(sampledAt));

        if (_hasFailedPoll)
        {
            HasUncertainInterval = true;
            RatePerHour = _window.GetRate(sampledAt);
            _hasFailedPoll = false;
        }
        else if (_baselineSnapshot is { } baseline && _baselineSampledAt is { } from)
        {
            var gain = XpProgressCalculator.Calculate(baseline, snapshot);
            HasUncertainInterval = gain.InvalidClasses.Count > 0;
            if (gain.ValidClassCount > 0)
            {
                _window.Add(from, sampledAt, gain.ValidGain);
                RatePerHour = _window.GetRate(sampledAt);
            }
        }

        LastSnapshot = snapshot;
        LastSuccessfulAt = sampledAt;
        _baselineSnapshot = snapshot;
        _baselineSampledAt = sampledAt;
        IsStale = false;
    }

    public void MarkFetchFailed(DateTimeOffset attemptedAt)
    {
        if (!IsStopped)
        {
            IsStale = true;
            _hasFailedPoll = true;
        }
    }

    public void Stop() => IsStopped = true;

    public void ResetRate()
    {
        _window.ClearIntervals();
        _baselineSnapshot = null;
        _baselineSampledAt = null;
        _hasFailedPoll = false;
        RatePerHour = null;
        HasUncertainInterval = false;
    }

    public void ResetAll()
    {
        ResetRate();
        _window.ResetSession();
    }
}
