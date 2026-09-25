namespace FourFoldAccountManager.Core.Timing;

public enum SpeedrunTimerState
{
    Ready,
    Running,
    Finished
}

public sealed record TimerLap(int Number, TimeSpan LapTime, TimeSpan SplitTotal);

public sealed record TimerSnapshot(
    SpeedrunTimerState State,
    TimeSpan Total,
    int CurrentLapNumber,
    TimeSpan CurrentLapTime,
    IReadOnlyList<TimerLap> Laps);

// A speedrun stopwatch: split starts the run and records laps, finish stops it, reset clears it.
// Times come from the monotonic TimeProvider timestamp, so wall-clock changes never affect a run.
public sealed class SpeedrunTimer
{
    private readonly TimeProvider _clock;
    private readonly List<TimerLap> _laps = [];
    private long _startTimestamp;
    private TimeSpan _finishedTotal;

    public SpeedrunTimer(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
    }

    public SpeedrunTimerState State { get; private set; } = SpeedrunTimerState.Ready;

    public bool Split()
    {
        switch (State)
        {
            case SpeedrunTimerState.Ready:
                _startTimestamp = _clock.GetTimestamp();
                State = SpeedrunTimerState.Running;
                return true;
            case SpeedrunTimerState.Running:
                RecordLap(Elapsed());
                return true;
            default:
                return false;
        }
    }

    public bool Finish()
    {
        if (State != SpeedrunTimerState.Running)
        {
            return false;
        }

        _finishedTotal = Elapsed();
        RecordLap(_finishedTotal);
        State = SpeedrunTimerState.Finished;
        return true;
    }

    public void Reset()
    {
        _laps.Clear();
        _finishedTotal = TimeSpan.Zero;
        State = SpeedrunTimerState.Ready;
    }

    public TimerSnapshot Snapshot()
    {
        var laps = _laps.ToArray();
        switch (State)
        {
            case SpeedrunTimerState.Running:
            {
                var total = Elapsed();
                var previousSplit = laps.Length > 0 ? laps[^1].SplitTotal : TimeSpan.Zero;
                return new TimerSnapshot(State, total, laps.Length + 1, total - previousSplit, laps);
            }
            case SpeedrunTimerState.Finished:
                return new TimerSnapshot(State, _finishedTotal, laps[^1].Number, laps[^1].LapTime, laps);
            default:
                return new TimerSnapshot(State, TimeSpan.Zero, 1, TimeSpan.Zero, laps);
        }
    }

    private TimeSpan Elapsed() => _clock.GetElapsedTime(_startTimestamp);

    private void RecordLap(TimeSpan splitTotal)
    {
        var previousSplit = _laps.Count > 0 ? _laps[^1].SplitTotal : TimeSpan.Zero;
        _laps.Add(new TimerLap(_laps.Count + 1, splitTotal - previousSplit, splitTotal));
    }
}
