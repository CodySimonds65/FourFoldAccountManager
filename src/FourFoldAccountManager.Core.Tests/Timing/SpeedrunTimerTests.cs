using FourFoldAccountManager.Core.Timing;
using Xunit;

namespace FourFoldAccountManager.Core.Tests.Timing;

public sealed class SpeedrunTimerTests
{
    private readonly ManualTimeProvider _clock = new();

    [Fact]
    public void NewTimerIsReadyAtZeroAndIgnoresFinish()
    {
        var timer = new SpeedrunTimer(_clock);

        Assert.False(timer.Finish());
        var snapshot = timer.Snapshot();

        Assert.Equal(SpeedrunTimerState.Ready, snapshot.State);
        Assert.Equal(TimeSpan.Zero, snapshot.Total);
        Assert.Equal(1, snapshot.CurrentLapNumber);
        Assert.Equal(TimeSpan.Zero, snapshot.CurrentLapTime);
        Assert.Empty(snapshot.Laps);
    }

    [Fact]
    public void FirstSplitStartsTheRun()
    {
        var timer = new SpeedrunTimer(_clock);

        Assert.True(timer.Split());
        _clock.Advance(TimeSpan.FromSeconds(1.5));
        var snapshot = timer.Snapshot();

        Assert.Equal(SpeedrunTimerState.Running, snapshot.State);
        Assert.Equal(TimeSpan.FromSeconds(1.5), snapshot.Total);
        Assert.Equal(1, snapshot.CurrentLapNumber);
        Assert.Equal(TimeSpan.FromSeconds(1.5), snapshot.CurrentLapTime);
        Assert.Empty(snapshot.Laps);
    }

    [Fact]
    public void EachLaterSplitRecordsALapFromThePreviousSplit()
    {
        var timer = new SpeedrunTimer(_clock);
        timer.Split();
        _clock.Advance(TimeSpan.FromSeconds(10));
        Assert.True(timer.Split());
        _clock.Advance(TimeSpan.FromSeconds(4));
        Assert.True(timer.Split());
        _clock.Advance(TimeSpan.FromSeconds(3));

        var snapshot = timer.Snapshot();

        Assert.Equal(
        [
            new TimerLap(1, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)),
            new TimerLap(2, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(14))
        ], snapshot.Laps);
        Assert.Equal(TimeSpan.FromSeconds(17), snapshot.Total);
        Assert.Equal(3, snapshot.CurrentLapNumber);
        Assert.Equal(TimeSpan.FromSeconds(3), snapshot.CurrentLapTime);
    }

    [Fact]
    public void FinishRecordsTheFinalLapAndFreezesTheTotal()
    {
        var timer = new SpeedrunTimer(_clock);
        timer.Split();
        _clock.Advance(TimeSpan.FromSeconds(10));
        timer.Split();
        _clock.Advance(TimeSpan.FromSeconds(5));

        Assert.True(timer.Finish());
        _clock.Advance(TimeSpan.FromMinutes(1));
        var snapshot = timer.Snapshot();

        Assert.Equal(SpeedrunTimerState.Finished, snapshot.State);
        Assert.Equal(TimeSpan.FromSeconds(15), snapshot.Total);
        Assert.Equal(
        [
            new TimerLap(1, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)),
            new TimerLap(2, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15))
        ], snapshot.Laps);
        Assert.Equal(2, snapshot.CurrentLapNumber);
        Assert.Equal(TimeSpan.FromSeconds(5), snapshot.CurrentLapTime);
    }

    [Fact]
    public void SplitAndFinishDoNothingAfterFinishUntilReset()
    {
        var timer = new SpeedrunTimer(_clock);
        timer.Split();
        _clock.Advance(TimeSpan.FromSeconds(8));
        timer.Finish();

        Assert.False(timer.Split());
        Assert.False(timer.Finish());
        Assert.Equal(SpeedrunTimerState.Finished, timer.State);

        timer.Reset();
        Assert.Equal(SpeedrunTimerState.Ready, timer.State);
        Assert.True(timer.Split());
        _clock.Advance(TimeSpan.FromSeconds(2));

        var snapshot = timer.Snapshot();
        Assert.Equal(TimeSpan.FromSeconds(2), snapshot.Total);
        Assert.Empty(snapshot.Laps);
    }

    [Fact]
    public void ResetWhileRunningClearsTheRun()
    {
        var timer = new SpeedrunTimer(_clock);
        timer.Split();
        _clock.Advance(TimeSpan.FromSeconds(3));
        timer.Split();
        _clock.Advance(TimeSpan.FromSeconds(3));

        timer.Reset();
        var snapshot = timer.Snapshot();

        Assert.Equal(SpeedrunTimerState.Ready, snapshot.State);
        Assert.Equal(TimeSpan.Zero, snapshot.Total);
        Assert.Equal(1, snapshot.CurrentLapNumber);
        Assert.Empty(snapshot.Laps);
    }

    [Fact]
    public void ResetAfterManyLapsStartsTheNextRunAtLapOne()
    {
        var timer = new SpeedrunTimer(_clock);
        timer.Split();
        for (var lap = 0; lap < 200; lap++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            timer.Split();
        }

        Assert.Equal(200, timer.Snapshot().Laps.Count);
        timer.Reset();
        timer.Split();
        _clock.Advance(TimeSpan.FromSeconds(1));
        timer.Split();

        Assert.Equal([new TimerLap(1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1))], timer.Snapshot().Laps);
    }
}
