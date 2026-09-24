namespace FourFoldAccountManager.Core.Leaderboard;

public static class LeaderboardPeriodWindow
{
    public static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) GetCurrent(
        LeaderboardPeriod period,
        DateTimeOffset nowUtc)
    {
        var utcNow = nowUtc.ToUniversalTime();
        var date = utcNow.UtcDateTime.Date;
        var today = new DateTimeOffset(date, TimeSpan.Zero);

        return period switch
        {
            LeaderboardPeriod.Daily => (today, today.AddDays(1)),
            LeaderboardPeriod.Weekly => GetWeek(today),
            LeaderboardPeriod.Monthly => GetMonth(today),
            _ => throw new ArgumentOutOfRangeException(nameof(period), period, "Unknown leaderboard period.")
        };
    }

    private static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) GetWeek(DateTimeOffset today)
    {
        var daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        var start = today.AddDays(-daysSinceMonday);
        return (start, start.AddDays(7));
    }

    private static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) GetMonth(DateTimeOffset today)
    {
        var start = new DateTimeOffset(today.Year, today.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var end = start.AddMonths(1);
        return (start, end);
    }
}
