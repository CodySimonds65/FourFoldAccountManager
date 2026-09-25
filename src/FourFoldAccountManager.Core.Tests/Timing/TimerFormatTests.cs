using FourFoldAccountManager.Core.Timing;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Timing;

public sealed class TimerFormatTests
{
    [Theory]
    [InlineData(0L, "0:00.00")]
    [InlineData(52_399_999L, "0:05.23")]
    [InlineData(7_545_600_000L, "12:34.56")]
    [InlineData(35_999_900_000L, "59:59.99")]
    [InlineData(36_000_000_000L, "1:00:00.00")]
    [InlineData(901_840_500_000L, "25:03:04.05")]
    [InlineData(-10_000_000L, "0:00.00")]
    public void FormatsTruncatedHundredthsWithHoursOnlyFromOneHour(long ticks, string expected) =>
        Assert.Equal(expected, TimerFormat.Format(TimeSpan.FromTicks(ticks)));
}
