namespace FourFoldAccountManager.Core.Tests.Timing;

// A monotonic clock the test advances by hand; one timestamp tick is one TimeSpan tick.
internal sealed class ManualTimeProvider : TimeProvider
{
    // A non-zero start proves a timestamp of 0 is not treated specially.
    private long _timestamp = 1_000;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _timestamp;

    public void Advance(TimeSpan by) => _timestamp += by.Ticks;
}
