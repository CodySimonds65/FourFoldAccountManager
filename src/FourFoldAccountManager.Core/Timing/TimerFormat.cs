using System.Globalization;

namespace FourFoldAccountManager.Core.Timing;

public static class TimerFormat
{
    private const long TicksPerHundredth = TimeSpan.TicksPerMillisecond * 10;

    // Truncates to hundredths so a displayed time is never ahead of the true time.
    public static string Format(TimeSpan value)
    {
        var hundredths = Math.Max(0, value.Ticks) / TicksPerHundredth;
        var fraction = hundredths % 100;
        var totalSeconds = hundredths / 100;
        var seconds = totalSeconds % 60;
        var minutes = totalSeconds / 60 % 60;
        var hours = totalSeconds / 3600;
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{seconds:00}.{fraction:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}:{seconds:00}.{fraction:00}");
    }
}
