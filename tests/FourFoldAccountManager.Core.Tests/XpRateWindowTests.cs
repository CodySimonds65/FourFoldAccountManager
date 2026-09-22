using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Core.Tests;

public class XpRateWindowTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 21, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void First_baseline_has_no_rate()
    {
        Assert.Null(new XpRateWindow().GetRate(Start));
    }

    [Fact]
    public void Unchanged_interval_reduces_rate_over_elapsed_time()
    {
        var window = new XpRateWindow();
        window.Add(Start, Start.AddMinutes(2), 100);
        Assert.Equal(3000, window.GetRate(Start.AddMinutes(2)));
        window.Add(Start.AddMinutes(2), Start.AddMinutes(4), 0);
        Assert.Equal(1500, window.GetRate(Start.AddMinutes(4)));
        Assert.Equal(100, window.SessionGain);
    }

    [Fact]
    public void Boundary_interval_is_clipped_proportionally()
    {
        var window = new XpRateWindow();
        window.Add(Start, Start.AddMinutes(20), 200);
        window.Add(Start.AddMinutes(20), Start.AddMinutes(70), 500);
        Assert.Equal(600, window.GetRate(Start.AddMinutes(70)));
        Assert.Equal(700, window.SessionGain);
    }

    [Fact]
    public void Invalid_intervals_are_rejected()
    {
        var window = new XpRateWindow();
        Assert.Throws<ArgumentOutOfRangeException>(() => window.Add(Start, Start, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => window.Add(Start, Start.AddMinutes(1), -1));
    }
}
