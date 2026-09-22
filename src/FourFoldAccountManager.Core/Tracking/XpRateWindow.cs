namespace FourFoldAccountManager.Core.Tracking;

public sealed record XpGainInterval(DateTimeOffset From, DateTimeOffset To, long Gain);

public sealed class XpRateWindow
{
    private readonly List<XpGainInterval> _intervals = [];

    public long SessionGain { get; private set; }

    public IReadOnlyList<XpGainInterval> Intervals => _intervals;

    public void Add(DateTimeOffset from, DateTimeOffset to, long gain)
    {
        if (to <= from || gain < 0)
        {
            throw new ArgumentOutOfRangeException(to <= from ? nameof(to) : nameof(gain));
        }

        SessionGain = checked(SessionGain + gain);
        _intervals.Add(new XpGainInterval(from, to, gain));
        Prune(to);
    }

    public double? GetRate(DateTimeOffset now)
    {
        Prune(now);
        var start = now - TimeSpan.FromHours(1);
        double gain = 0;
        double coveredSeconds = 0;
        foreach (var interval in _intervals)
        {
            var from = interval.From > start ? interval.From : start;
            var to = interval.To < now ? interval.To : now;
            var seconds = (to - from).TotalSeconds;
            if (seconds <= 0)
            {
                continue;
            }

            gain += interval.Gain * seconds / (interval.To - interval.From).TotalSeconds;
            coveredSeconds += seconds;
        }

        return coveredSeconds > 0 ? gain / coveredSeconds * 3600 : null;
    }

    public void ClearIntervals() => _intervals.Clear();

    public void ResetSession()
    {
        ClearIntervals();
        SessionGain = 0;
    }

    private void Prune(DateTimeOffset now) =>
        _intervals.RemoveAll(interval => interval.To <= now - TimeSpan.FromHours(1));
}
