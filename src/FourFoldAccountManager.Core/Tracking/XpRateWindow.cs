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

    public double? GetRate(DateTimeOffset now) => GetRate(now, null, TimeSpan.Zero);

    // The rate over the hour ending at `now`. `openFrom` adds the stretch from then to `now` as watched time that gained
    // nothing (the time since the live game feed's last fight). Null until the watched time in that hour reaches
    // `minCovered`.
    public double? GetRate(DateTimeOffset now, DateTimeOffset? openFrom, TimeSpan minCovered)
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

        if (openFrom is { } open && open < now)
        {
            coveredSeconds += (now - (open > start ? open : start)).TotalSeconds;
        }

        return coveredSeconds > 0 && coveredSeconds >= minCovered.TotalSeconds ? gain / coveredSeconds * 3600 : null;
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
