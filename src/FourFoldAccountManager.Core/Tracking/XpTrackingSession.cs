namespace FourFoldAccountManager.Core.Tracking;

public sealed class XpTrackingSession
{
    private readonly XpRateWindow _window = new();

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
    public bool IsStale { get; private set; }
    public bool IsStopped { get; private set; }
    public bool HasUncertainInterval { get; private set; }

    public void ApplySnapshot(PlayerProgressSnapshot snapshot, DateTimeOffset sampledAt)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (IsStopped) throw new InvalidOperationException("The tracking session has stopped.");
        if (LastSuccessfulAt is { } previousTime && sampledAt <= previousTime)
            throw new ArgumentOutOfRangeException(nameof(sampledAt));

        if (LastSnapshot is { } previous && LastSuccessfulAt is { } from)
        {
            var gain = XpProgressCalculator.Calculate(previous, snapshot);
            HasUncertainInterval = gain.InvalidClasses.Count > 0;
            if (gain.ValidGain > 0 || gain.InvalidClasses.Count < snapshot.Classes.Count + snapshot.InvalidClasses.Count)
            {
                _window.Add(from, sampledAt, gain.ValidGain);
                RatePerHour = _window.GetRate(sampledAt);
            }
        }

        LastSnapshot = snapshot;
        LastSuccessfulAt = sampledAt;
        IsStale = false;
    }

    public void MarkFetchFailed(DateTimeOffset attemptedAt)
    {
        if (!IsStopped) IsStale = true;
    }

    public void Stop() => IsStopped = true;
}
