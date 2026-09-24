namespace FourFoldAccountManager.Leaderboard.Service.Collection;

public sealed class LeaderboardSamplingSchedule
{
    private readonly object _gate = new();
    private CompletedPass? _lastPass;

    internal CompletedPass? LastPass
    {
        get { lock (_gate) return _lastPass; }
    }

    internal void Complete(DateTimeOffset finishedAtUtc, TimeSpan elapsed, HashSet<int> sampledPlayerIds)
    {
        lock (_gate)
            _lastPass = new CompletedPass(finishedAtUtc, elapsed, sampledPlayerIds);
    }

    internal void Clear()
    {
        lock (_gate) _lastPass = null;
    }

    internal sealed record CompletedPass(
        DateTimeOffset FinishedAtUtc, TimeSpan Elapsed, IReadOnlySet<int> SampledPlayerIds);
}
